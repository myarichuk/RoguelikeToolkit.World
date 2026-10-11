// Shared drawing helpers for the procedural ink stamps: a seeded PRNG, polyline
// geometry, hand-wobble, tapered (filled) pen strokes, hatching and compact SVG
// path serialization. Pure TS: no DOM, no Node APIs (runs in workers and node).

export type PaletteToken =
  | 'ink' | 'paper' | 'shade' | 'light' | 'foliage' | 'foliageDark'
  | 'water' | 'roof' | 'wall' | 'snow' | 'sand' | 'none';

export interface StampShape { d: string; fill: PaletteToken; stroke: PaletteToken; width?: number; opacity?: number; }

/** A w x h box, y down. `anchor` is the point (box units) that sits on the map location. */
export interface Stamp { w: number; h: number; anchor: [number, number]; shapes: StampShape[]; }

export type Pt = [number, number];

// ---------------------------------------------------------------- random

/** FNV-1a over the string forms of the parts, finished with a murmur3 avalanche. */
export function mixSeed(...parts: Array<string | number>): number {
  let h = 0x811c9dc5;
  for (const p of parts) {
    const s = String(p);
    for (let i = 0; i < s.length; i++) {
      h ^= s.charCodeAt(i);
      h = Math.imul(h, 0x01000193);
    }
    h ^= 0x1f;
    h = Math.imul(h, 0x01000193);
  }
  h ^= h >>> 16; h = Math.imul(h, 0x85ebca6b);
  h ^= h >>> 13; h = Math.imul(h, 0xc2b2ae35);
  h ^= h >>> 16;
  return h >>> 0;
}

/** mulberry32. */
export class Rng {
  private s: number;
  constructor(seed: number) { this.s = seed >>> 0; }
  next(): number {
    this.s = (this.s + 0x6d2b79f5) >>> 0;
    let t = this.s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  }
  range(a: number, b: number): number { return a + (b - a) * this.next(); }
  /** Uniform in [-a, a]. */
  jit(a: number): number { return (this.next() * 2 - 1) * a; }
  /** Integer in [a, b] inclusive. */
  int(a: number, b: number): number { return a + Math.floor(this.next() * (b - a + 1)); }
  chance(p: number): boolean { return this.next() < p; }
  sign(): 1 | -1 { return this.next() < 0.5 ? -1 : 1; }
  pick<T>(arr: readonly T[]): T { return arr[Math.floor(this.next() * arr.length)] as T; }
  /** Roughly normal, mean 0, sd ~0.5, clamped to [-1.5, 1.5]. */
  gauss(): number { return this.next() + this.next() + this.next() - 1.5; }
}

// ---------------------------------------------------------------- geometry

export const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;
export const lerpPt = (a: Pt, b: Pt, t: number): Pt => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
export const dist = (a: Pt, b: Pt): number => { const dx = b[0] - a[0], dy = b[1] - a[1]; return Math.sqrt(dx * dx + dy * dy); };
export const clamp = (v: number, a: number, b: number): number => (v < a ? a : v > b ? b : v);
export const add = (a: Pt, b: Pt): Pt => [a[0] + b[0], a[1] + b[1]];

export function polyLength(pts: readonly Pt[]): number {
  let l = 0;
  for (let i = 1; i < pts.length; i++) l += dist(pts[i - 1] as Pt, pts[i] as Pt);
  return l;
}

/** Point at arc-length fraction t of a polyline. */
export function pointAt(pts: readonly Pt[], t: number): Pt {
  const total = polyLength(pts);
  let target = clamp(t, 0, 1) * total;
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1] as Pt, b = pts[i] as Pt;
    const l = dist(a, b);
    if (target <= l || i === pts.length - 1) return l > 0 ? lerpPt(a, b, clamp(target / l, 0, 1)) : [a[0], a[1]];
    target -= l;
  }
  const p = pts[0] as Pt;
  return [p[0], p[1]];
}

/** Resample to n points evenly spaced by arc length. */
export function resample(pts: readonly Pt[], n: number): Pt[] {
  const out: Pt[] = [];
  const total = polyLength(pts);
  if (total === 0 || pts.length < 2) { for (let i = 0; i < n; i++) out.push([...(pts[0] as Pt)] as Pt); return out; }
  let seg = 1, acc = 0;
  for (let i = 0; i < n; i++) {
    const target = (total * i) / (n - 1);
    while (seg < pts.length - 1 && acc + dist(pts[seg - 1] as Pt, pts[seg] as Pt) < target) {
      acc += dist(pts[seg - 1] as Pt, pts[seg] as Pt);
      seg++;
    }
    const a = pts[seg - 1] as Pt, b = pts[seg] as Pt;
    const l = dist(a, b);
    out.push(l > 0 ? lerpPt(a, b, clamp((target - acc) / l, 0, 1)) : [a[0], a[1]]);
  }
  return out;
}

/** Unit normals (left-hand, i.e. rotate tangent by -90deg in y-down space) per vertex. */
export function normals(pts: readonly Pt[], closed = false): Pt[] {
  const n = pts.length;
  const out: Pt[] = [];
  for (let i = 0; i < n; i++) {
    const p = closed ? (pts[(i - 1 + n) % n] as Pt) : (pts[Math.max(0, i - 1)] as Pt);
    const q = closed ? (pts[(i + 1) % n] as Pt) : (pts[Math.min(n - 1, i + 1)] as Pt);
    let dx = q[0] - p[0], dy = q[1] - p[1];
    const l = Math.hypot(dx, dy) || 1;
    dx /= l; dy /= l;
    out.push([dy, -dx]);
  }
  return out;
}

/** n values in [-1,1], smoothed so neighbouring values correlate (cheap value noise). */
export function smoothNoise(rng: Rng, n: number, passes = 1): number[] {
  let v: number[] = [];
  for (let i = 0; i < n; i++) v.push(rng.jit(1));
  for (let p = 0; p < passes; p++) {
    const w: number[] = [];
    for (let i = 0; i < n; i++) {
      const a = v[Math.max(0, i - 1)] as number, b = v[i] as number, c = v[Math.min(n - 1, i + 1)] as number;
      w.push((a + 2 * b + c) * 0.5);
    }
    v = w;
  }
  return v;
}

/** Hand wobble: resample every ~seg units and push points sideways by smooth noise. Ends stay put. */
export function wobble(pts: readonly Pt[], rng: Rng, amp: number, seg = 4, closed = false): Pt[] {
  const len = polyLength(closed ? [...pts, pts[0] as Pt] : pts);
  const n = Math.max(closed ? 6 : 3, Math.min(64, Math.round(len / seg) + 1));
  const src = closed ? resample([...pts, pts[0] as Pt], n + 1).slice(0, n) : resample(pts, n);
  const nn = normals(src, closed);
  const noise = smoothNoise(rng, n, 1);
  return src.map((p, i) => {
    const e = closed ? 1 : Math.min(1, i / 1.5, (n - 1 - i) / 1.5);
    const k = amp * (noise[i] as number) * e;
    const nv = nn[i] as Pt;
    return [p[0] + nv[0] * k, p[1] + nv[1] * k] as Pt;
  });
}

/** Just jitter vertices (keeps the vertex count). */
export function jitter(pts: readonly Pt[], rng: Rng, amp: number): Pt[] {
  return pts.map((p) => [p[0] + rng.jit(amp), p[1] + rng.jit(amp)] as Pt);
}

export type WidthFn = (t: number) => number;

/** Standard pen-pressure profile: swells from tip to full width and back. */
export function penProfile(w: number, start = 0.25, end = 0.25, tip = 0.08): WidthFn {
  return (t: number) => {
    const a = start > 0 ? Math.min(1, t / start) : 1;
    const b = end > 0 ? Math.min(1, (1 - t) / end) : 1;
    return w * (tip + (1 - tip) * Math.sqrt(Math.max(0, Math.min(a, b))));
  };
}

/**
 * A filled tapered stroke around a centerline: returns a closed polygon (left side out,
 * right side back). Width is the full width; the function gets the arc-length fraction.
 */
export function taper(center: readonly Pt[], width: number | WidthFn, samples = 0): Pt[] {
  const pts = samples > 0 ? resample(center, samples) : center.slice();
  const wf: WidthFn = typeof width === 'number' ? penProfile(width) : width;
  const nn = normals(pts);
  const total = polyLength(pts) || 1;
  const left: Pt[] = [], right: Pt[] = [];
  let acc = 0;
  for (let i = 0; i < pts.length; i++) {
    if (i > 0) acc += dist(pts[i - 1] as Pt, pts[i] as Pt);
    const p = pts[i] as Pt, nv = nn[i] as Pt;
    const h = wf(acc / total) / 2;
    left.push([p[0] + nv[0] * h, p[1] + nv[1] * h]);
    right.push([p[0] - nv[0] * h, p[1] - nv[1] * h]);
  }
  return left.concat(right.reverse());
}

export function signedArea(pts: readonly Pt[]): number {
  const n = pts.length;
  if (n < 3) return 0;
  let a = 0;
  let p = pts[n - 1] as Pt;
  for (let i = 0; i < n; i++) {
    const q = pts[i] as Pt;
    a += p[0] * q[1] - q[0] * p[1];
    p = q;
  }
  return a / 2;
}

/** Make a polygon wind positively so merged ink subpaths never cancel under nonzero fill. */
export function orient(pts: Pt[]): Pt[] { return signedArea(pts) < 0 ? pts.reverse() : pts; }

export function arc(cx: number, cy: number, rx: number, ry: number, a0: number, a1: number, n: number): Pt[] {
  const out: Pt[] = [];
  for (let i = 0; i < n; i++) {
    const a = lerp(a0, a1, n === 1 ? 0 : i / (n - 1));
    out.push([cx + Math.cos(a) * rx, cy + Math.sin(a) * ry]);
  }
  return out;
}

export function ellipse(cx: number, cy: number, rx: number, ry: number, n = 16): Pt[] {
  return arc(cx, cy, rx, ry, 0, Math.PI * 2 * (1 - 1 / n), n);
}

export function quad(a: Pt, c: Pt, b: Pt, n: number): Pt[] {
  const out: Pt[] = [];
  for (let i = 0; i < n; i++) {
    const t = i / (n - 1), u = 1 - t;
    out.push([u * u * a[0] + 2 * u * t * c[0] + t * t * b[0], u * u * a[1] + 2 * u * t * c[1] + t * t * b[1]]);
  }
  return out;
}

export function translate(pts: readonly Pt[], dx: number, dy: number): Pt[] { return pts.map((p) => [p[0] + dx, p[1] + dy] as Pt); }
export function mirrorX(pts: readonly Pt[], cx = 0): Pt[] { return pts.map((p) => [2 * cx - p[0], p[1]] as Pt); }

/** Where a polyline (monotone-ish in y) crosses height y; returns index of the segment and the point. */
export function crossY(pts: readonly Pt[], y: number): { i: number; p: Pt } | null {
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1] as Pt, b = pts[i] as Pt;
    if ((a[1] - y) * (b[1] - y) <= 0 && a[1] !== b[1]) return { i, p: lerpPt(a, b, (y - a[1]) / (b[1] - a[1])) };
  }
  return null;
}

/**
 * An open pen loop around a closed outline: starts at fraction `start` of the loop and
 * runs `1 - gap` of the way round, tapering at both ends - a closed shape drawn the way
 * a pen does it, leaving a small highlight gap.
 */
export function loopStroke(closedPts: readonly Pt[], start: number, gap: number, width: number | WidthFn): Pt[] {
  const n = closedPts.length;
  const loop: Pt[] = [];
  for (let i = 0; i <= n; i++) loop.push(closedPts[i % n] as Pt);
  const steps = Math.max(10, Math.min(28, n));
  const center: Pt[] = [];
  for (let i = 0; i < steps; i++) {
    const t = (start + (1 - gap) * (i / (steps - 1))) % 1;
    center.push(pointAt(loop, t));
  }
  return taper(center, typeof width === 'number' ? penProfile(width, 0.12, 0.12, 0.1) : width);
}

// ---------------------------------------------------------------- serialization

export interface SubPath { pts: Pt[]; closed: boolean; smooth: boolean; }

/** Round to 1 decimal, shortest form: 0.5 -> ".5", -0.5 -> "-.5", 2.0 -> "2". */
export function fmt(v: number): string { return fmtTenths(Math.round(v * 10)); }

function fmtTenths(t: number): string {
  if (t === 0) return '0';
  const neg = t < 0;
  const a = neg ? -t : t;
  const ip = (a / 10) | 0, fr = a - ip * 10;
  const body = fr === 0 ? String(ip) : ip === 0 ? '.' + fr : ip + '.' + fr;
  return neg ? '-' + body : body;
}

/** Integer tenths, so relative steps are exact and never drift. */
type Ti = [number, number];

class PathWriter {
  out = '';
  private afterCmd = true;
  private lastDot = false;
  private cur: Ti = [0, 0];
  private tok(s: string): void {
    const c = s.charCodeAt(0);
    const dot = s.indexOf('.') >= 0;
    // separators are needed only where the number would otherwise run on
    if (!(this.afterCmd || c === 45 /* - */ || (c === 46 /* . */ && this.lastDot))) this.out += ' ';
    this.out += s;
    this.afterCmd = false;
    this.lastDot = dot;
  }
  cmd(c: string): void { this.out += c; this.afterCmd = true; this.lastDot = false; }
  private tenths(v: number): string { return fmtTenths(Math.round(v)); }
  /** Absolute point (moveto). */
  abs(p: Ti): void { this.tok(this.tenths(p[0])); this.tok(this.tenths(p[1])); this.cur = p; }
  /** Relative point; `move` advances the current point (endpoints) or not (control points). */
  rel(p: Ti, move = true): void {
    this.tok(this.tenths(p[0] - this.cur[0]));
    this.tok(this.tenths(p[1] - this.cur[1]));
    if (move) this.cur = p;
  }
  /** Relative control point, measured from the segment start. */
  ctl(p: Ti): void { this.rel(p, false); }
  close(start: Ti): void { this.cmd('z'); this.cur = start; }
}

const toT = (p: Pt, dx: number, dy: number): Ti => [Math.round((p[0] + dx) * 10), Math.round((p[1] + dy) * 10)];
/** Snap to the even-tenths grid so that spline midpoints land exactly on the 0.1 grid. */
const toT2 = (p: Pt, dx: number, dy: number): Ti => [Math.round((p[0] + dx) * 5) * 2, Math.round((p[1] + dy) * 5) * 2];
const midT = (a: Ti, b: Ti): Ti => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];

/**
 * Serialize subpaths (relative coordinates, 1 decimal). Smooth paths are quadratic
 * B-splines through the midpoints of the control polygon: `Q c m` once, then `T m` per
 * vertex - T's reflected control is exactly the next vertex, so the output costs one
 * point per vertex and stays inside the hull (bounding boxes stay honest).
 */
export function pathD(subs: readonly SubPath[], dx = 0, dy = 0): string {
  const w = new PathWriter();
  for (const s of subs) {
    const n = s.pts.length;
    if (n < 2) continue;
    if (!s.smooth || n < 3) {
      const p = s.pts.map((q) => toT(q, dx, dy));
      w.cmd('M'); w.abs(p[0]!);
      w.cmd('l');
      for (let i = 1; i < n; i++) w.rel(p[i]!);
      if (s.closed) w.close(p[0]!);
      continue;
    }
    const p = s.pts.map((q) => toT2(q, dx, dy));
    if (s.closed) {
      const start = midT(p[n - 1]!, p[0]!);
      w.cmd('M'); w.abs(start);
      w.cmd('q'); w.ctl(p[0]!); w.rel(midT(p[0]!, p[1]!));
      w.cmd('t');
      for (let i = 1; i < n; i++) w.rel(midT(p[i]!, p[(i + 1) % n]!));
      w.close(start);
    } else {
      w.cmd('M'); w.abs(p[0]!);
      w.cmd('q'); w.ctl(p[1]!);
      if (n === 3) { w.rel(p[2]!); continue; }
      w.rel(midT(p[1]!, p[2]!));
      w.cmd('t');
      for (let i = 2; i < n - 2; i++) w.rel(midT(p[i]!, p[i + 1]!));
      w.rel(p[n - 1]!);
    }
  }
  return w.out;
}

// ---------------------------------------------------------------- builder

interface Draft { subs: SubPath[]; fill: PaletteToken; stroke: PaletteToken; width?: number; opacity?: number; }

/**
 * Collects shapes in authoring coordinates (any origin, y down), then `finish` fits the
 * box around them and serializes. Shapes paint in insertion order.
 */
export class InkBuilder {
  private drafts: Draft[] = [];

  add(subs: SubPath | SubPath[], fill: PaletteToken, stroke: PaletteToken, width?: number, opacity?: number): this {
    const raw = Array.isArray(subs) ? subs : [subs];
    // closed subpaths all wind the same way so merged fills never cancel under nonzero
    const list = raw.filter((sp) => sp.pts.length > 1).map((sp) => (sp.closed && signedArea(sp.pts) < 0 ? { ...sp, pts: sp.pts.slice().reverse() } : sp));
    if (list.length === 0) return this;
    const d: Draft = { subs: list, fill, stroke };
    if (stroke !== 'none' && width !== undefined) d.width = Math.round(width * 100) / 100;
    if (opacity !== undefined && opacity < 1) d.opacity = Math.round(opacity * 100) / 100;
    this.drafts.push(d);
    return this;
  }

  /** Fill one or more closed polygons, no outline. */
  fill(polys: Pt[] | Pt[][], token: PaletteToken, smooth = true, opacity?: number): this {
    return this.add(asList(polys).map((pts) => ({ pts, closed: true, smooth })), token, 'none', undefined, opacity);
  }

  /** Filled ink (tapered strokes etc.), merged into one shape. */
  ink(polys: Pt[] | Pt[][], smooth = true, opacity?: number, token: PaletteToken = 'ink'): this {
    return this.fill(asList(polys), token, smooth, opacity);
  }

  /** Plain open strokes (fine hatching, small detail). */
  lines(lines: Pt[][], width: number, token: PaletteToken = 'ink', opacity?: number, smooth = false): this {
    return this.add(lines.filter((l) => l.length > 1).map((pts) => ({ pts, closed: false, smooth })), 'none', token, width, opacity);
  }

  /** Filled closed polygon(s) with a plain ink outline (small architecture). */
  outlined(polys: Pt[] | Pt[][], fill: PaletteToken, width: number, smooth = false, stroke: PaletteToken = 'ink'): this {
    return this.add(asList(polys).map((pts) => ({ pts, closed: true, smooth })), fill, stroke, width);
  }

  finish(anchor: Pt, margin = 1.5): Stamp {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity, maxW = 0;
    for (const d of this.drafts) {
      if (d.stroke !== 'none') maxW = Math.max(maxW, d.width ?? 1);
      for (const s of d.subs) for (const p of s.pts) {
        if (p[0] < minX) minX = p[0];
        if (p[0] > maxX) maxX = p[0];
        if (p[1] < minY) minY = p[1];
        if (p[1] > maxY) maxY = p[1];
      }
    }
    if (!isFinite(minX)) { minX = minY = 0; maxX = maxY = 1; }
    const m = margin + maxW / 2;
    const dx = Math.round((m - minX) * 10) / 10, dy = Math.round((m - minY) * 10) / 10;
    const shapes: StampShape[] = mergeDrafts(this.drafts).map((d) => {
      const s: StampShape = { d: pathD(d.subs, dx, dy), fill: d.fill, stroke: d.stroke };
      if (d.width !== undefined) s.width = d.width;
      if (d.opacity !== undefined) s.opacity = d.opacity;
      return s;
    }).filter((s) => s.d.length > 0);
    return {
      w: Math.ceil((maxX - minX + 2 * m) * 10) / 10,
      h: Math.ceil((maxY - minY + 2 * m) * 10) / 10,
      anchor: [Math.round((anchor[0] + dx) * 10) / 10, Math.round((anchor[1] + dy) * 10) / 10],
      shapes,
    };
  }
}

type BBox = [number, number, number, number];

function bboxOf(d: Draft): BBox {
  let a = Infinity, b = Infinity, c = -Infinity, e = -Infinity;
  for (const s of d.subs) for (const p of s.pts) {
    if (p[0] < a) a = p[0];
    if (p[0] > c) c = p[0];
    if (p[1] < b) b = p[1];
    if (p[1] > e) e = p[1];
  }
  const pad = (d.stroke !== 'none' ? (d.width ?? 1) / 2 : 0) + 0.3;
  return [a - pad, b - pad, c + pad, e + pad];
}

const hits = (p: BBox, q: BBox): boolean => p[0] < q[2] && q[0] < p[2] && p[1] < q[3] && q[1] < p[3];

/**
 * Fold each shape into the most recent earlier shape of identical style when nothing
 * painted in between overlaps it, so the painted result is unchanged but the output
 * has far fewer <path> elements (a city's houses collapse into a handful of layers).
 */
function mergeDrafts(drafts: readonly Draft[]): Draft[] {
  const out: Array<{ d: Draft; bb: BBox; key: string }> = [];
  for (const src of drafts) {
    const d: Draft = { ...src, subs: src.subs.slice() };
    const bb = bboxOf(d);
    const key = `${d.fill}|${d.stroke}|${d.width ?? ''}|${d.opacity ?? ''}`;
    // overlapping same-style shapes merge losslessly only when single-role and opaque
    const overlapOk = d.opacity === undefined && (d.stroke === 'none' || d.fill === 'none');
    let target = -1;
    for (let j = out.length - 1; j >= 0; j--) {
      const o = out[j]!;
      const overlap = hits(o.bb, bb);
      if (o.key === key && (!overlap || overlapOk)) { target = j; break; }
      if (overlap) break;
    }
    if (target >= 0) {
      const o = out[target]!;
      o.d.subs.push(...d.subs);
      o.bb = [Math.min(o.bb[0], bb[0]), Math.min(o.bb[1], bb[1]), Math.max(o.bb[2], bb[2]), Math.max(o.bb[3], bb[3])];
    } else out.push({ d, bb, key });
  }
  return out.map((o) => o.d);
}

function asList(polys: Pt[] | Pt[][]): Pt[][] {
  if (polys.length === 0) return [];
  return typeof (polys[0] as Pt | Pt[])[0] === 'number' ? [polys as Pt[]] : (polys as Pt[][]);
}

// ---------------------------------------------------------------- reusable marks

/** A short ground stroke: horizontal tapered dash. */
export function groundDash(rng: Rng, x0: number, x1: number, y: number, w: number): Pt[] {
  const mid: Pt = [lerp(x0, x1, 0.5), y + rng.jit(0.25)];
  return taper([[x0, y + rng.jit(0.2)], mid, [x1, y + rng.jit(0.2)]], penProfile(w, 0.45, 0.45, 0.05), 6);
}

/** Parallel hatch segments between two guide polylines (a -> b at matching fractions). */
export function hatchBetween(rng: Rng, a: readonly Pt[], b: readonly Pt[], n: number, t0: number, t1: number, len: [number, number], drop = 0): Pt[][] {
  const out: Pt[][] = [];
  for (let i = 0; i < n; i++) {
    const t = lerp(t0, t1, (i + rng.range(0.25, 0.75)) / n);
    const p = pointAt(a, t);
    const q = pointAt(b, clamp(t + drop, 0, 1));
    const k = rng.range(len[0], len[1]);
    out.push([lerpPt(p, q, 0.06), lerpPt(p, q, k)]);
  }
  return out;
}
