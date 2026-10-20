// Trees, plants and ground-cover stamps (and the rocks and crevasses that sit with them).
import { InkBuilder, Rng, lerp, lerpPt, pointAt, taper, penProfile, arc, ellipse, quad, loopStroke, groundDash, translate, orient } from '../ink';
import type { Pt, Stamp } from '../ink';
import { MID, FINE } from './common';

// ================================================================ trees & vegetation

/** Scalloped blob (tree crowns, bushes, smoke): cusps between lumps. */
export function lumpy(r: Rng, cx: number, cy: number, rx: number, ry: number, lumps: number, amp: number, flatBottom: number): Pt[] {
  const per = 3;
  const out: Pt[] = [];
  const a0 = r.range(0, Math.PI * 2);
  const radii: number[] = [];
  for (let i = 0; i < lumps; i++) radii.push(r.range(0.88, 1.08));
  for (let i = 0; i < lumps * per; i++) {
    const li = Math.floor(i / per), f = (i % per) / per;
    const a = a0 + (Math.PI * 2 * i) / (lumps * per);
    const rr = lerp(radii[li]!, radii[(li + 1) % lumps]!, f) * (1 + amp * Math.sin(Math.PI * f));
    let y = cy + Math.sin(a) * ry * rr;
    if (flatBottom > 0) y = Math.min(y, cy + ry * flatBottom);
    out.push([cx + Math.cos(a) * rx * rr, y]);
  }
  return out;
}

/** Loop fraction at which a pen loop should start so its gap sits toward `angle` (upper-left light). */
export function gapStart(pts: readonly Pt[], cx: number, cy: number, angle = -2.2): number {
  let best = 0, bd = Infinity;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i]!;
    let d = Math.abs(Math.atan2(p[1] - cy, p[0] - cx) - angle);
    if (d > Math.PI) d = Math.PI * 2 - d;
    if (d < bd) { bd = d; best = i; }
  }
  return best / pts.length;
}

export function shadeCrescent(pts: readonly Pt[], cx: number, cy: number, ox: number, oy: number, k: number): Pt[] {
  // the part of the blob facing down-right, with an inner edge pulled toward the light
  const sel: Pt[] = [];
  const n = pts.length;
  let start = 0, best = -Infinity;
  for (let i = 0; i < n; i++) { const p = pts[i]!; const s = -(p[0] - cx) - (p[1] - cy) * 0.3; if (s > best) { best = s; start = i; } }
  for (let i = 0; i < n; i++) {
    const p = pts[(start + i) % n]!;
    const s = (p[0] - cx) * 0.8 + (p[1] - cy);
    if (s > -0.15 * Math.abs(ox)) sel.push(p);
  }
  if (sel.length < 3) return [];
  const inner = sel.map(([x, y]) => [lerp(cx + ox, x, k), lerp(cy + oy, y, k)] as Pt).reverse();
  return sel.concat(inner);
}

export function genConifer(r: Rng): Stamp {
  const b = new InkBuilder();
  const H = r.range(26, 34);
  const trunk = r.range(2.5, 4);
  const hw = H * r.range(0.24, 0.32);
  const lean = r.jit(1.2);
  const tiers = r.int(3, 5);
  const A: Pt = [lean, -H];
  const side = (s: 1 | -1): Pt[] => {
    const pts: Pt[] = [A];
    for (let i = 1; i <= tiers; i++) {
      const f = i / tiers;
      const y = lerp(-H, -trunk, f) + r.jit(0.4);
      const x = lerp(lean, 0, f) + s * (hw * Math.pow(f, 0.85) + r.jit(0.6));
      if (i > 1) {
        const yi = y - (H - trunk) / tiers * r.range(0.45, 0.6);
        pts.push([lerp(lean, 0, f) + s * hw * Math.pow(f, 0.85) * r.range(0.38, 0.5), yi]);
      }
      pts.push([x, y]);
    }
    return pts;
  };
  const Lp = side(-1), Rp = side(1);
  const bottom: Pt = [r.jit(0.5), -trunk + 0.8];
  const crown = Lp.concat([bottom]).concat(Rp.slice(1).reverse());
  b.ink(taper([[0, 0.4], [lean * 0.1, -trunk - 3]], penProfile(1.6, 0, 0.4, 0.4)));
  b.fill(crown, 'foliage', false);
  const mid: Pt[] = [];
  for (let i = 0; i <= 4; i++) mid.push([lerp(lean, bottom[0], i / 4) + (i > 0 && i < 4 ? r.jit(0.5) : 0), lerp(-H, bottom[1], i / 4)]);
  b.fill(mid.concat(Rp.slice(1).reverse()), 'foliageDark', false);
  const hatch: Pt[][] = [];
  for (let i = 1; i <= tiers; i++) {
    const f = (i - 0.35) / tiers;
    const y = lerp(-H, -trunk, f);
    const x = lerp(lean, 0, f);
    hatch.push([[x + hw * f * 0.15, y], [x + hw * f * 0.55, y + 1.6]]);
  }
  b.lines(hatch, FINE * 0.8, 'ink', 0.8);
  const ink: Pt[][] = [];
  ink.push(taper(Lp.concat([bottom]), penProfile(MID * 1.1, 0.02, 0.15, 0.2)));
  ink.push(taper(Rp.concat([bottom]), penProfile(MID * 1.4, 0.02, 0.15, 0.2)));
  ink.push(groundDash(r, -r.range(2, 4), r.range(3, 6), 0.6, MID * 0.7));
  b.ink(ink, false);
  return b.finish([0, 0]);
}

export function genDeciduous(r: Rng): Stamp {
  const b = new InkBuilder();
  const rx = r.range(9, 13), ry = r.range(8.5, 11.5);
  const th = r.range(6, 10);
  const cx = r.jit(1), cy = -th - ry * 0.8;
  const tw = r.range(2.2, 3.2);
  const top: Pt = [cx * 0.6, cy + ry * 0.3];
  b.ink(taper([[0, 0.3], [r.jit(0.4), -th * 0.5], top], (t) => lerp(tw, tw * 0.45, t) * (t < 0.12 ? 1 + (0.12 - t) * 4 : 1)));
  const crown = lumpy(r, cx, cy, rx, ry, r.int(6, 10), r.range(0.12, 0.22), 0.85);
  b.fill(crown, 'foliage', false);
  const sh = shadeCrescent(crown, cx, cy, -rx * 0.3, -ry * 0.35, 0.72);
  if (sh.length) b.fill(sh, 'foliageDark', true);
  const ink: Pt[][] = [];
  ink.push(loopStroke(crown, gapStart(crown, cx, cy, r.range(-2.4, -1.9)), r.range(0.05, 0.1), MID * 1.25));
  const marks = r.int(2, 4);
  for (let i = 0; i < marks; i++) {
    const mx = cx + rx * r.range(-0.15, 0.5), my = cy + ry * r.range(-0.05, 0.5);
    const rr = r.range(1.8, 2.8);
    ink.push(taper(arc(mx, my, rr, rr * 0.75, Math.PI + r.range(0.2, 0.5), Math.PI * 2 - r.range(0.1, 0.4), 6), penProfile(FINE * 1.2, 0.3, 0.3, 0.1)));
  }
  ink.push(groundDash(r, -r.range(3, 5), r.range(3, 7), 0.6, MID * 0.7));
  b.ink(ink);
  return b.finish([0, 0]);
}

export function genPalm(r: Rng): Stamp {
  const b = new InkBuilder();
  const H = r.range(22, 28);
  const lean = r.sign() * r.range(3, 8);
  const T: Pt = [lean, -H];
  const trunkC = quad([0, 0], [lean * 0.1 + r.jit(1), -H * 0.55], T, 8);
  b.ink(taper(trunkC, (t) => lerp(2.0, 1.1, t)));
  const ticks: Pt[][] = [];
  for (let i = 1; i < 7; i++) {
    const q = pointAt(trunkC, i / 7.3);
    ticks.push([[q[0] - 1.1, q[1] + 0.3], [q[0] + 1.1, q[1] - 0.2]]);
  }
  b.lines(ticks, 0.4, 'paper');
  const nf = r.int(5, 7);
  const fronds: Array<{ a: number; pts: Pt[] }> = [];
  for (let i = 0; i < nf; i++) {
    const a = lerp(-Math.PI + 0.2, -0.2, (i + r.range(0.3, 0.7)) / nf) + (lean > 0 ? 0.1 : -0.1);
    const len = r.range(12, 16) * (1 - 0.25 * Math.abs(Math.sin(a)));
    const dx = Math.cos(a), dy = Math.sin(a);
    const c: Pt = [T[0] + dx * len * 0.6, T[1] + dy * len * 0.6 - 3];
    const e: Pt = [T[0] + dx * len, T[1] + dy * len * 0.3 + r.range(4, 8)];
    fronds.push({ a: Math.abs(a + Math.PI / 2), pts: quad(T, c, e, 7) });
  }
  fronds.sort((x, y) => x.a - y.a);
  for (const f of fronds) {
    const leaf = taper(f.pts, (t) => 3.2 * Math.pow(Math.sin(Math.PI * Math.pow(t, 0.7)), 0.8) + 0.15);
    b.outlined(leaf, 'foliage', FINE * 1.1, true);
    b.lines([f.pts.slice(0, 6)], FINE * 0.8, 'ink', 0.9, true);
  }
  const nuts: Pt[][] = [];
  for (let i = 0; i < r.int(2, 3); i++) nuts.push(ellipse(T[0] + r.jit(1.5), T[1] + 1.2 + r.jit(0.5), 0.9, 0.9, 6));
  b.ink(nuts);
  b.ink(groundDash(r, -r.range(2, 4), r.range(3, 6), 0.6, MID * 0.7));
  return b.finish([0, 0]);
}

export function genDeadTree(r: Rng): Stamp {
  const b = new InkBuilder();
  const ink: Pt[][] = [];
  const H = r.range(22, 30);
  const branch = (p: Pt, ang: number, len: number, w: number, depth: number): void => {
    const e: Pt = [p[0] + Math.cos(ang) * len, p[1] + Math.sin(ang) * len];
    const m = lerpPt(p, e, 0.5);
    m[0] += r.jit(len * 0.12); m[1] += r.jit(len * 0.08);
    ink.push(taper([p, m, e], penProfile(w, 0, 0.75, depth === 0 ? 0.05 : 0.3)));
    if (depth <= 0) return;
    const kids = depth >= 2 ? 2 : r.int(1, 2);
    for (let i = 0; i < kids; i++) {
      const spread = (i === 0 ? -1 : 1) * r.range(0.35, 0.7);
      const from = lerpPt(p, e, depth >= 2 ? r.range(0.8, 1) : r.range(0.6, 1));
      branch(from, ang + spread + r.jit(0.15), len * r.range(0.5, 0.72), w * 0.62, depth - 1);
    }
  };
  branch([0, 0.3], -Math.PI / 2 + r.jit(0.15), H * 0.45, 2.8, 3);
  // roots
  ink.push(taper([[0, 0], [-2.5, 0.6], [-4.5, 0.9]], penProfile(1.2, 0, 0.8, 0.1)));
  ink.push(taper([[0, 0], [2.5, 0.5], [5, 1]], penProfile(1.2, 0, 0.8, 0.1)));
  ink.push(groundDash(r, -r.range(5, 7), r.range(5, 8), 1.4, MID * 0.6));
  b.ink(ink);
  return b.finish([0, 0]);
}

export function genShrub(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(2, 4);
  const w = r.range(14, 22);
  const blobs: Array<{ x: number; y: number; rx: number; ry: number }> = [];
  for (let i = 0; i < n; i++) {
    const x = lerp(-w / 2, w / 2, n === 1 ? 0.5 : i / (n - 1)) * 0.6 + r.jit(1);
    const rx = r.range(3.5, 5.5);
    blobs.push({ x, y: -rx * 0.7 + r.jit(0.4), rx, ry: rx * r.range(0.7, 0.9) });
  }
  blobs.sort((a, c) => c.ry - a.ry);
  for (const bl of blobs) {
    const pts = lumpy(r, bl.x, bl.y, bl.rx, bl.ry, r.int(4, 6), 0.18, 0.8);
    b.fill(pts, 'foliage', false);
    const sh = shadeCrescent(pts, bl.x, bl.y, -bl.rx * 0.3, -bl.ry * 0.4, 0.7);
    if (sh.length) b.fill(sh, 'foliageDark');
    b.ink(loopStroke(pts, gapStart(pts, bl.x, bl.y), 0.1, MID * 0.8));
  }
  b.ink(groundDash(r, -w / 2 - 1, w / 2 + 1, 0.4, MID * 0.7));
  return b.finish([0, 0]);
}

export function blade(r: Rng, x: number, h: number, ang: number, w: number): Pt[] {
  const e: Pt = [x + Math.cos(ang) * h, Math.sin(ang) * h];
  const c: Pt = [lerp(x, e[0], 0.5) + r.jit(h * 0.1), lerp(0, e[1], 0.5)];
  return taper(quad([x, 0], c, e, 5), penProfile(w, 0, 0.9, 0.05));
}

export function genGrassTuft(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(5, 8);
  const ink: Pt[][] = [];
  for (let i = 0; i < n; i++) {
    const f = (i + 0.5) / n - 0.5;
    const ang = -Math.PI / 2 + f * r.range(1.6, 2.4) + r.jit(0.15);
    ink.push(blade(r, f * 4 + r.jit(0.5), r.range(5, 10) * (1 - Math.abs(f) * 0.6), ang, r.range(0.55, 0.7)));
  }
  ink.push(groundDash(r, -4.5, 4.5, 0.4, 0.7));
  b.ink(ink);
  return b.finish([0, 0]);
}

export function genReeds(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(4, 7);
  const ink: Pt[][] = [];
  const heads: Pt[][] = [];
  for (let i = 0; i < n; i++) {
    const x = lerp(-8, 8, (i + r.range(0.2, 0.8)) / n);
    const h = r.range(12, 22);
    const ang = -Math.PI / 2 + r.jit(0.18) + x * 0.012;
    const e: Pt = [x + Math.cos(ang) * h, Math.sin(ang) * h];
    const stem = quad([x, 0], [lerp(x, e[0], 0.5) + r.jit(1), e[1] * 0.5], e, 5);
    ink.push(taper(stem, penProfile(0.8, 0, 0.5, 0.25)));
    if (r.chance(0.6)) {
      const q = pointAt(stem, 0.78);
      heads.push(ellipse(q[0], q[1], 0.95, 2.6, 8).map(([px, py]) => [px + (py - q[1]) * Math.cos(ang) * -0.9, py] as Pt));
    } else if (r.chance(0.6)) {
      const q = pointAt(stem, r.range(0.3, 0.5));
      ink.push(blade(r, 0, h * 0.45, ang + r.sign() * r.range(0.5, 0.9), 0.7).map(([px, py]) => [px + q[0], py + q[1]] as Pt));
    }
  }
  b.ink(ink);
  if (heads.length) b.ink(heads);
  const water: Pt[][] = [];
  const wl = r.int(2, 3);
  for (let i = 0; i < wl; i++) {
    const y = 0.6 + i * 1.6, x0 = -10 + r.jit(2) + i * 2, x1 = 10 + r.jit(2) - i * 2;
    water.push([[x0, y], [lerp(x0, x1, 0.5), y + r.jit(0.3)], [x1, y]]);
  }
  b.lines(water, FINE, 'ink', 0.8, true);
  return b.finish([0, 0]);
}

export function genDunes(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(2, 3);
  const ink: Pt[][] = [];
  const dunes: Array<{ x: number; y: number; w: number; h: number }> = [];
  for (let i = 0; i < n; i++) dunes.push({ x: r.jit(10) + (i - (n - 1) / 2) * 12, y: -i * r.range(4, 6), w: r.range(22, 34), h: r.range(4, 7) });
  dunes.sort((a, c) => a.y - c.y);
  const hatch: Pt[][] = [];
  for (const d of dunes) {
    const pk = r.range(0.45, 0.65);
    const crest: Pt[] = [];
    for (let i = 0; i <= 8; i++) {
      const t = i / 8;
      const u = t < pk ? t / pk : (1 - t) / (1 - pk);
      crest.push([d.x - d.w / 2 + d.w * t, d.y - d.h * Math.pow(Math.sin((Math.PI / 2) * u), 1.3) + (t < pk ? 0 : (t - pk) * d.h * 0.4)]);
    }
    const ip = Math.round(pk * 8);
    const lee = crest.slice(ip);
    const back = lee.map(([x, y], i) => [x - 2 - i * 0.3, y + lerp(d.h * 0.9, 0.2, i / (lee.length - 1))] as Pt).reverse();
    b.fill(lee.concat(back), 'shade', true, 0.8);
    for (let i = 0; i < 4; i++) {
      const q = pointAt(lee, (i + 0.5) / 4);
      hatch.push([[q[0] - 0.3, q[1] + 0.9], [q[0] - 1.3, q[1] + 0.9 + d.h * 0.35 * (1 - i / 5)]]);
    }
    ink.push(taper(crest, penProfile(MID * 1.15, 0.3, 0.25, 0.05)));
    if (r.chance(0.7)) {
      const s = crest.slice(1, ip + 1).map(([x, y]) => [x - 1.2, y + d.h * 0.45] as Pt);
      ink.push(taper(s, penProfile(FINE, 0.4, 0.4, 0.05)));
    }
  }
  b.lines(hatch, FINE * 0.9, 'ink', 0.8);
  b.ink(ink);
  return b.finish([0, 0]);
}

export function genCactus(r: Rng): Stamp {
  const b = new InkBuilder();
  const H = r.range(18, 26);
  const tw = r.range(3.8, 5);
  const trunk: Pt[] = [[0, 0], [r.jit(0.4), -H * 0.5], [r.jit(0.6), -H]];
  const arms: Pt[][] = [];
  const na = r.int(1, 2);
  for (let i = 0; i < na; i++) {
    const s = i === 0 ? r.sign() : -1 * (arms.length ? Math.sign(arms[0]![1]![0]) || 1 : 1);
    const y0 = -H * r.range(0.35, 0.6);
    const out = r.range(4, 6) * s;
    const up = r.range(5, 9);
    arms.push([[0, y0], [out * 0.8, y0 + 0.2], [out, y0 - 1.5], [out, y0 - up]]);
  }
  const all = [trunk, ...arms];
  b.lines(all, tw + 1.6, 'ink', undefined, true);
  b.lines(arms, tw * 0.75, 'foliage', undefined, true);
  b.lines([trunk], tw, 'foliage', undefined, true);
  b.lines([translate(trunk, tw * 0.25, 0).slice(0, 3)], tw * 0.35, 'foliageDark', undefined, true);
  b.lines(arms.map((a) => translate(a, tw * 0.15 * Math.sign(a[1]![0] || 1), 0).slice(2)), tw * 0.25, 'foliageDark', undefined, true);
  const ribs: Pt[][] = [[[-0.8, -0.5], [-0.8 + r.jit(0.2), -H + 1]], [[0.9, -0.5], [0.9, -H + 1.5]]];
  b.lines(ribs, FINE * 0.7, 'ink', 0.8);
  b.ink(groundDash(r, -r.range(4, 6), r.range(4, 7), 0.5, MID * 0.8));
  return b.finish([0, 0]);
}

/** Irregular faceted boulder outline, flat-ish bottom at y. */
function boulder(r: Rng, cx: number, y: number, rad: number): Pt[] {
  const n = r.int(6, 8);
  const pts: Pt[] = [];
  const a0 = r.range(0, 0.6);
  for (let i = 0; i < n; i++) {
    const a = Math.PI + a0 + (Math.PI * 2 * i) / n;
    const rr = rad * r.range(0.75, 1.1);
    pts.push([cx + Math.cos(a) * rr * 1.15, Math.min(y, y - rad * 0.75 + Math.sin(a) * rr * 0.85)]);
  }
  return pts;
}

/** Closed polygon drawn as one pen loop with sharp corners (no resampling). */
function polyLoop(pts: readonly Pt[], start: number, w: number): Pt[] {
  const n = pts.length;
  const c: Pt[] = [];
  for (let i = 0; i <= n; i++) c.push(pts[(start + i) % n]!);
  c.push(lerpPt(pts[start % n]!, pts[(start + 1) % n]!, 0.25));
  return taper(c, penProfile(w, 0.08, 0.08, 0.1));
}

export function drawBoulder(b: InkBuilder, r: Rng, cx: number, y: number, rad: number, w = MID): void {
  const pts = boulder(r, cx, y, rad);
  b.fill(pts, 'paper', false);
  // shadow facets: right/lower part with an inner vertex toward the light
  const core: Pt = [cx - rad * 0.15 + r.jit(rad * 0.1), y - rad * 0.95];
  const rightSide = pts.filter((p) => p[0] > core[0] + rad * 0.05);
  rightSide.sort((p, q) => Math.atan2(p[1] - y + rad, p[0] - cx) - Math.atan2(q[1] - y + rad, q[0] - cx));
  if (rightSide.length >= 2) {
    b.fill(orient([core, ...rightSide, [cx + rad * 0.1, y]]), 'shade', false);
  }
  const ink: Pt[][] = [polyLoop(pts, r.int(0, pts.length - 1), w)];
  const top = pts.reduce((m, p) => (p[1] < m[1] ? p : m), pts[0]!);
  ink.push(taper([top, lerpPt(top, core, 0.5), core, [core[0] + r.jit(1), y - rad * 0.3]], penProfile(FINE * 1.3, 0.1, 0.5, 0.1)));
  b.ink(ink, false);
  const hatch: Pt[][] = [];
  for (let i = 0; i < 3; i++) {
    const x = lerp(core[0], cx + rad, (i + 1) / 4);
    hatch.push([[x, y - rad * 0.55], [x + rad * 0.2, y - rad * 0.15]]);
  }
  b.lines(hatch, FINE * 0.8, 'ink', 0.75);
}

export function genRock(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(2, 4);
  const list: Array<{ x: number; y: number; rad: number }> = [];
  for (let i = 0; i < n; i++) list.push({ x: (i - (n - 1) / 2) * 7 + r.jit(2), y: r.range(-2, 1.5), rad: i === 0 ? r.range(5, 7) : r.range(2.5, 5) });
  list.sort((a, c) => a.y - c.y);
  for (const s of list) drawBoulder(b, r, s.x, s.y, s.rad);
  b.ink(groundDash(r, -n * 4 - 3, n * 4 + 3, 2, MID * 0.6));
  return b.finish([0, 0]);
}

export function genCrevasse(r: Rng): Stamp {
  const b = new InkBuilder();
  const ink: Pt[][] = [];
  const n = r.int(2, 4);
  for (let i = 0; i < n; i++) {
    const y = (i - n / 2) * 4 + r.jit(1);
    const x0 = r.range(-16, -8), x1 = r.range(8, 16);
    const k = r.int(6, 9);
    const line: Pt[] = [];
    for (let j = 0; j <= k; j++) line.push([lerp(x0, x1, j / k) + r.jit(0.6), y + (j % 2 ? 1 : -1) * r.range(0.4, 1.3) + Math.sin(j) * 0.6]);
    ink.push(taper(line, penProfile(r.range(0.9, 1.4), 0.4, 0.4, 0.03)));
    if (r.chance(0.6)) {
      const q = line[r.int(2, k - 2)]!;
      ink.push(taper([q, [q[0] + r.jit(2), q[1] + r.sign() * r.range(2, 3.5)]], penProfile(0.6, 0, 0.8, 0.05)));
    }
  }
  b.ink(ink, false);
  return b.finish([0, 0]);
}
