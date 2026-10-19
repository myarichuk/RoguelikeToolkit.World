import type opentype from 'opentype.js';
import type { Rect } from '../core/geo';

export interface LayoutInput {
  text: string;
  /** Cap height in map units. */
  size: number;
  /** Extra tracking in em. */
  spacing: number;
  uppercase: boolean;
  x: number; y: number;
  angle: number;
  /** Arc bend, -1..1 (positive arches upward). */
  curve: number;
  /** Optional baseline polyline (absolute x,y pairs). */
  path?: number[];
}

export interface LabelGeometry {
  /** SVG path data in absolute map units. */
  d: string;
  /** Axis-aligned bounds (x unwrapped like the input). */
  bounds: Rect;
  /** Advance width of the text along its baseline (map units). */
  width: number;
}

interface PlacedGlyph { glyph: opentype.Glyph; s: number; adv: number; }

/** Ratio cap height / em, from OS/2 when present. */
function capRatio(font: opentype.Font): number {
  const os2 = (font.tables as { os2?: { sCapHeight?: number } }).os2;
  return os2?.sCapHeight ? os2.sCapHeight / font.unitsPerEm : 0.68;
}

const stripMarks = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '');

/** Glyph for a character, falling back to its unaccented form when the font lacks it. */
function glyphFor(font: opentype.Font, ch: string): opentype.Glyph {
  const g = font.charToGlyph(ch);
  if (g.index !== 0 || ch === ' ') return g;
  const plain = stripMarks(ch);
  return plain && plain !== ch ? font.charToGlyph(plain[0]) : g;
}

/**
 * Lays out a label as glyph outlines. Three baselines: straight (curve 0), circular arc
 * (curve != 0), or a polyline (rivers). Glyphs are placed by arc length and rotated to the local
 * tangent; the text is centered on the anchor (or on the path's midpoint) both along and across
 * the baseline. Text running right-to-left along a path is flipped so it never reads upside down.
 */
export function layoutLabel(font: opentype.Font, inp: LayoutInput): LabelGeometry {
  const text = inp.uppercase ? inp.text.toUpperCase() : inp.text;
  const fontSize = inp.size / capRatio(font);
  const scale = fontSize / font.unitsPerEm;
  const track = inp.spacing * fontSize;
  const placed: PlacedGlyph[] = [];
  let s = 0;
  let prev: opentype.Glyph | null = null;
  for (const ch of text) {
    const g = glyphFor(font, ch);
    if (prev) s += font.getKerningValue(prev, g) * scale;
    const adv = (g.advanceWidth ?? font.unitsPerEm * 0.5) * scale;
    placed.push({ glyph: g, s, adv });
    s += adv + track;
    prev = g;
  }
  const width = Math.max(0, s - track);
  const lift = inp.size / 2; // center caps vertically on the baseline position

  // Baseline as a function of arc length t in [-width/2, width/2]: point + tangent angle.
  let baseline: (t: number) => { x: number; y: number; a: number };
  if (inp.path && inp.path.length >= 4) {
    baseline = pathBaseline(inp.path, width, inp.size * 0.95);
  } else if (Math.abs(inp.curve) > 1e-3 && width > 0) {
    const theta = inp.curve * 1.1;           // total bend over the text width
    const R = width / Math.abs(theta);
    const sign = Math.sign(theta);
    const ca = Math.cos(inp.angle), sa = Math.sin(inp.angle);
    baseline = (t) => {
      const phi = (t / R);
      // Local frame: x along, y down. Positive curve arches up (center below the text).
      const lx = R * Math.sin(phi), ly = sign * R * (1 - Math.cos(phi));
      return { x: inp.x + ca * lx - sa * ly, y: inp.y + sa * lx + ca * ly, a: inp.angle + sign * phi };
    };
  } else {
    const ca = Math.cos(inp.angle), sa = Math.sin(inp.angle);
    baseline = (t) => ({ x: inp.x + ca * t, y: inp.y + sa * t, a: inp.angle });
  }

  let d = '';
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  const r = (v: number) => Math.round(v * 100) / 100;
  for (const pg of placed) {
    if (!pg.glyph.path || pg.adv === 0) continue;
    const mid = pg.s + pg.adv / 2 - width / 2;
    const b = baseline(mid);
    const ca = Math.cos(b.a), sa = Math.sin(b.a);
    // Glyph-local: origin at the glyph's horizontal center on the (lifted) baseline.
    const tx = (gx: number, gy: number): [number, number] => {
      const lx = gx - pg.adv / 2, ly = gy + lift;
      const X = b.x + ca * lx - sa * ly, Y = b.y + sa * lx + ca * ly;
      if (X < x0) x0 = X; if (X > x1) x1 = X; if (Y < y0) y0 = Y; if (Y > y1) y1 = Y;
      return [r(X), r(Y)];
    };
    const path = pg.glyph.getPath(0, 0, fontSize);
    for (const c of path.commands) {
      switch (c.type) {
        case 'M': { const [X, Y] = tx(c.x, c.y); d += `M${X} ${Y}`; break; }
        case 'L': { const [X, Y] = tx(c.x, c.y); d += `L${X} ${Y}`; break; }
        case 'Q': { const [a, b2] = tx(c.x1, c.y1); const [X, Y] = tx(c.x, c.y); d += `Q${a} ${b2} ${X} ${Y}`; break; }
        case 'C': {
          const [a, b2] = tx(c.x1, c.y1); const [e, f] = tx(c.x2, c.y2); const [X, Y] = tx(c.x, c.y);
          d += `C${a} ${b2} ${e} ${f} ${X} ${Y}`; break;
        }
        case 'Z': d += 'Z'; break;
      }
    }
  }
  if (!Number.isFinite(x0)) { x0 = x1 = inp.x; y0 = y1 = inp.y; }
  return { d, bounds: { x0, y0, x1, y1 }, width };
}

/**
 * Baseline along a polyline (a river), as a function of arc length from the text's center. The
 * path is resampled, smoothed over about two cap heights (a name follows the river's course, not
 * each meander) and offset to the side above it; glyphs are then spaced by arc length along that
 * offset line, so they keep even spacing on the inside of a bend instead of piling up. The result
 * is centered on the path's midpoint and extrapolated straight past its ends.
 */
function pathBaseline(path: number[], textWidth: number, offset: number): (t: number) => { x: number; y: number; a: number } {
  let raw: [number, number][] = [];
  for (let i = 0; i + 1 < path.length; i += 2) raw.push([path[i], path[i + 1]]);
  if (raw[raw.length - 1][0] < raw[0][0]) raw = raw.reverse(); // read left to right
  const step = Math.max(0.25, offset * 0.2);
  const line = smooth(resample(raw, step), Math.max(1, Math.round((offset * 2) / step)));
  // Offset to the left of the direction of travel (above, for left-to-right text).
  const off: [number, number][] = line.map((p, i) => {
    const a = line[Math.max(0, i - 1)], b = line[Math.min(line.length - 1, i + 1)];
    const ang = Math.atan2(b[1] - a[1], b[0] - a[0]);
    return [p[0] + Math.sin(ang) * offset, p[1] - Math.cos(ang) * offset];
  });
  const cum = [0];
  for (let i = 1; i < off.length; i++) cum.push(cum[i - 1] + Math.hypot(off[i][0] - off[i - 1][0], off[i][1] - off[i - 1][1]));
  const total = cum[cum.length - 1];
  const dir = (a: [number, number], b: [number, number]): [number, number] => {
    const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy);
    return l > 0 ? [dx / l, dy / l] : [1, 0];
  };
  const sDir = off.length > 1 ? dir(off[0], off[1]) : [1, 0] as [number, number];
  const eDir = off.length > 1 ? dir(off[off.length - 2], off[off.length - 1]) : [1, 0] as [number, number];
  const at = (s: number): [number, number] => {
    // A name longer than its river keeps going straight past the ends instead of piling its glyphs there.
    if (s <= 0 || off.length < 2) return [off[0][0] + sDir[0] * s, off[0][1] + sDir[1] * s];
    if (s >= total) { const d = s - total, l = off[off.length - 1]; return [l[0] + eDir[0] * d, l[1] + eDir[1] * d]; }
    let lo = 1, hi = cum.length - 1;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (cum[mid] < s) lo = mid + 1; else hi = mid; }
    const seg = cum[lo] - cum[lo - 1] || 1, f = (s - cum[lo - 1]) / seg;
    return [off[lo - 1][0] + (off[lo][0] - off[lo - 1][0]) * f, off[lo - 1][1] + (off[lo][1] - off[lo - 1][1]) * f];
  };
  const center = total / 2, win = Math.max(textWidth * 0.04, offset * 0.5);
  return (t) => {
    const s = center + t;
    const [ax, ay] = at(s - win), [bx, by] = at(s + win);
    const [x, y] = at(s);
    return { x, y, a: Math.atan2(by - ay, bx - ax) };
  };
}

/** Points every `step` map units along a polyline (ends kept). */
function resample(pts: [number, number][], step: number): [number, number][] {
  const out: [number, number][] = [pts[0]];
  let carry = 0;
  for (let i = 1; i < pts.length; i++) {
    const [ax, ay] = pts[i - 1], [bx, by] = pts[i];
    const len = Math.hypot(bx - ax, by - ay);
    let d = step - carry;
    while (d <= len) { const f = d / len; out.push([ax + (bx - ax) * f, ay + (by - ay) * f]); d += step; }
    carry = len - (d - step);
  }
  const last = pts[pts.length - 1], tail = out[out.length - 1];
  if (tail[0] !== last[0] || tail[1] !== last[1]) out.push(last);
  return out;
}

/** Three passes of a moving average of radius `r` points (indices clamped at the ends). */
function smooth(pts: [number, number][], r: number): [number, number][] {
  let cur = pts;
  for (let pass = 0; pass < 3; pass++) {
    cur = cur.map((_, i) => {
      let sx = 0, sy = 0, n = 0;
      for (let k = i - r; k <= i + r; k++) { const p = cur[Math.max(0, Math.min(cur.length - 1, k))]; sx += p[0]; sy += p[1]; n++; }
      return [sx / n, sy / n] as [number, number];
    });
  }
  return cur;
}
