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

/** Arc-length parametrized polyline baseline centered on the path's midpoint, extrapolated past its ends. */
function pathBaseline(path: number[], textWidth: number, offset: number): (t: number) => { x: number; y: number; a: number } {
  let pts: [number, number][] = [];
  for (let i = 0; i + 1 < path.length; i += 2) pts.push([path[i], path[i + 1]]);
  if (pts[pts.length - 1][0] < pts[0][0]) pts = pts.reverse(); // read left to right
  const cum = [0];
  for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
  const total = cum[cum.length - 1] || 1;
  // End directions for extrapolation (first/last non-degenerate segment; +x when the path is a point).
  const dir = (a: [number, number], b: [number, number]): [number, number] => {
    const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy);
    return l > 0 ? [dx / l, dy / l] : [1, 0];
  };
  let si = 0;
  while (si < pts.length - 2 && pts[si + 1][0] === pts[si][0] && pts[si + 1][1] === pts[si][1]) si++;
  let ei = pts.length - 1;
  while (ei > 1 && pts[ei][0] === pts[ei - 1][0] && pts[ei][1] === pts[ei - 1][1]) ei--;
  const sDir = dir(pts[si], pts[si + 1]), eDir = dir(pts[ei - 1], pts[ei]);
  const at = (s: number): [number, number] => {
    // A name longer than its river keeps going straight past the segment instead of piling its glyphs at the ends.
    if (s <= 0) return [pts[0][0] + sDir[0] * s, pts[0][1] + sDir[1] * s];
    if (s >= total) { const d = s - total, l = pts[pts.length - 1]; return [l[0] + eDir[0] * d, l[1] + eDir[1] * d]; }
    const t = s;
    let i = 1;
    while (i < cum.length - 1 && cum[i] < t) i++;
    const seg = cum[i] - cum[i - 1] || 1, f = (t - cum[i - 1]) / seg;
    return [pts[i - 1][0] + (pts[i][0] - pts[i - 1][0]) * f, pts[i - 1][1] + (pts[i][1] - pts[i - 1][1]) * f];
  };
  const center = total / 2, win = Math.max(textWidth * 0.08, 1.5);
  return (t) => {
    const s = center + t;
    const [ax, ay] = at(s - win), [bx, by] = at(s + win);
    const a = Math.atan2(by - ay, bx - ax);
    const [x, y] = at(s);
    // Sit just above the path (rivers are drawn on it).
    return { x: x + Math.sin(a) * offset, y: y - Math.cos(a) * offset, a };
  };
}
