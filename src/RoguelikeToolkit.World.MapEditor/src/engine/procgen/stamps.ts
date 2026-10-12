// Procedural hand-inked fantasy-map stamps (classic sepia ink-map style).
//
// Every generator is deterministic: (id, seed, params) -> byte-identical Stamp.
// Authoring scale: UNITS_PER_MAP_UNIT box units per map unit at `defaultSize`, i.e. a
// stamp's nominal visual height is about defaultSize * UNITS_PER_MAP_UNIT box units, so
// ink line weights match across generators when each is placed at its defaultSize.
// To place a stamp at `size` map units: scale = size / (defaultSize * UNITS_PER_MAP_UNIT)
// (see `stampScale`), then put `anchor` on the map point.

import {
  InkBuilder, Rng, mixSeed, lerp, lerpPt, clamp, pointAt, wobble, smoothNoise, taper, penProfile,
  arc, ellipse, quad, crossY, loopStroke, groundDash, jitter, polyLength, translate, orient,
} from './ink';
import type { Pt, PaletteToken, Stamp, WidthFn } from './ink';

export type { PaletteToken, StampShape, Stamp } from './ink';

export type StampCategory = 'mountain' | 'hill' | 'tree' | 'vegetation' | 'settlement' | 'structure' | 'water' | 'terrain' | 'decoration';

export interface GeneratorInfo {
  id: string;
  label: string;
  category: StampCategory;
  /** Map units, ~ visual height on a 2048x1024 world map. */
  defaultSize: number;
  params?: Record<string, { min: number; max: number; default: number }>;
  generate(seed: number, params?: Record<string, number>): Stamp;
}

export const UNITS_PER_MAP_UNIT = 5;

export const DEFAULT_PALETTE: Record<PaletteToken, string> = {
  ink: '#2b2119',
  paper: '#e9dcbc',
  shade: '#c7b28a',
  light: '#f5ecd4',
  foliage: '#b3b07c',
  foliageDark: '#8c8a5c',
  water: '#b9c2ad',
  roof: '#b0694a',
  wall: '#efe3c6',
  snow: '#f8f3e6',
  sand: '#dcc58f',
  none: 'none',
};

// line weights (box units)
const OUT = 1.7; // silhouettes
const MID = 1.0; // interior structure
const FINE = 0.5; // hatching / detail
const ARCH = 0.6; // architecture outlines (plain strokes)

type Params = Record<string, number>;
type ParamSpec = Record<string, { min: number; max: number; default: number }>;

function resolveParams(spec: ParamSpec | undefined, given: Params | undefined): Params {
  const out: Params = {};
  if (spec) for (const k of Object.keys(spec)) {
    const s = spec[k]!;
    const v = given?.[k];
    out[k] = typeof v === 'number' && isFinite(v) ? clamp(v, s.min, s.max) : s.default;
  }
  return out;
}

const smooth01 = (t: number): number => t * t * (3 - 2 * t);

// ================================================================ mountains

interface PeakOpts { cx: number; base: number; H: number; hw: number; snow: number; W: number; ground: boolean; }

/** One slope from the summit S down to the foot F: concave, with an optional shoulder knob. */
function slope(r: Rng, S: Pt, F: Pt, H: number): Pt[] {
  const n = r.int(5, 7);
  const p = r.range(1.2, 1.8);
  const shoulder = r.chance(0.65) ? r.range(0.22, 0.6) : -1;
  const sh = r.range(0.06, 0.13);
  const noise = smoothNoise(r, n + 1, 1);
  const pts: Pt[] = [S];
  for (let i = 1; i < n; i++) {
    const t = clamp(i / n + r.jit(0.25 / n), 0.03, 0.97);
    let yf = 1 - Math.pow(1 - t, p);
    if (shoulder > 0) { const d = (t - shoulder) / 0.11; yf -= sh * Math.exp(-d * d) * (1 - t); }
    pts.push([lerp(S[0], F[0], t), lerp(S[1], F[1], yf) + noise[i]! * H * 0.02]);
  }
  pts.push(F);
  return pts;
}

function peak(b: InkBuilder, r: Rng, o: PeakOpts): void {
  const { cx, base, H, hw, W } = o;
  const S: Pt = [cx + r.jit(0.2) * hw, base - H];
  const L: Pt = [cx - hw * r.range(0.85, 1.1), base];
  const R: Pt = [cx + hw * r.range(0.95, 1.25), base];
  const left = wobble(slope(r, S, L, H), r, 0.3, 6); // S -> L
  const right = wobble(slope(r, S, R, H), r, 0.3, 6); // S -> R
  const sil = left.slice().reverse().concat(right.slice(1)); // L -> S -> R

  // spine: the ridge line falling from the summit, splitting lit and shadow faces
  const RB: Pt = [S[0] + (R[0] - S[0]) * r.range(0.14, 0.42), base - H * r.range(0, 0.04)];
  const bow = r.range(-0.08, 0.14) * H;
  const rn = smoothNoise(r, 7, 1);
  const ridge0: Pt[] = [];
  for (let i = 0; i < 7; i++) {
    const t = i / 6;
    const e = i > 0 && i < 6 ? rn[i]! * H * 0.02 : 0;
    ridge0.push([lerp(S[0], RB[0], t) + Math.sin(Math.PI * t) * t * bow + e, lerp(S[1], RB[1], Math.pow(t, 0.9))]);
  }
  const ridge = wobble(ridge0, r, 0.25, 6);

  // paper body (occludes what is behind) and the shadow face
  b.fill(sil, 'paper');
  b.fill(ridge.concat(right.slice().reverse().slice(0, -1)), 'shade');

  // hatching: downhill strokes on the shadow face, from the spine toward the outline
  const nh = Math.round(H / 6) + r.int(0, 2);
  const hatch: Pt[][] = [];
  for (let i = 0; i < nh; i++) {
    const t = lerp(0.08, 0.96, (i + r.range(0.2, 0.8)) / nh);
    const A = pointAt(ridge, t);
    const B = pointAt(right, clamp(t + r.range(0.05, 0.15), 0, 1));
    const k = r.range(0.45, 0.85) * (0.55 + 0.45 * t);
    const P0 = lerpPt(A, B, 0.12), P1 = lerpPt(A, B, k);
    hatch.push([P0, P1]);
  }
  b.lines(hatch, FINE, 'ink', 0.9);

  // snow cap
  if (o.snow > 0) {
    const ys = S[1] + H * (0.18 + 0.2 * o.snow) * r.range(0.85, 1.15);
    const cl = crossY(left, ys), cr = crossY(right, ys), cg = crossY(ridge, ys);
    if (cl && cr && cg) {
      const teeth = 3 + r.int(0, 2) + Math.round(o.snow * 2);
      const Z: Pt[] = [cl.p];
      for (let i = 1; i < teeth * 2; i++) {
        const t = i / (teeth * 2);
        const down = i % 2 === 1 ? H * r.range(0.04, 0.09) : -H * r.range(0, 0.03);
        Z.push([lerp(cl.p[0], cr.p[0], t), ys + down]);
      }
      Z.push(cr.p);
      const capL = left.slice(0, cl.i).reverse(); // near the crossing -> S
      const capR = right.slice(1, cr.i); // below S -> near the crossing
      b.fill([cl.p, ...capL, ...capR, cr.p, ...Z.slice(1, -1).reverse()], 'snow');
      // shaded part of the cap, right of the spine
      const zr = Z.filter((q) => q[0] > cg.p[0]);
      b.fill([...ridge.slice(0, cg.i), cg.p, ...zr, ...capR.slice().reverse()], 'shade', true, 0.55);
      b.lines([Z], FINE * 0.9, 'ink', 0.75, true);
    }
  }

  // ink: outline (lighter on the lit side, heavier on the shadow side), spine, spurs, ground
  const ink: Pt[][] = [];
  const tS = polyLength(left) / polyLength(sil);
  const outW: WidthFn = (t) => {
    const env = penProfile(1, 0.2, 0.16, 0.04)(t);
    const m = 0.8 + 0.42 * smooth01(clamp((t - tS + 0.05) / 0.1, 0, 1));
    return W * env * m;
  };
  ink.push(taper(sil, outW));
  ink.push(taper(ridge, penProfile(W * 0.8, 0.02, 0.7, 0.05)));
  const spurs = r.int(0, 2);
  for (let i = 0; i < spurs; i++) {
    const s0 = pointAt(left, r.range(0.3, 0.65));
    const len = H * r.range(0.1, 0.2);
    const a: Pt = [s0[0] + 0.8, s0[1] + 0.4];
    const e: Pt = [a[0] + len * r.range(0.35, 0.7), a[1] + len];
    ink.push(taper([a, lerpPt(a, e, 0.5), e], penProfile(W * 0.45, 0.05, 0.7, 0.05)));
  }
  const flecks = r.int(1, 3);
  for (let i = 0; i < flecks; i++) {
    const t = r.range(0.5, 0.88);
    const q = pointAt(left, t), q2 = pointAt(left, t + 0.08);
    const off = r.range(2, 4.5);
    const a: Pt = [q[0] + off, q[1] + off * 0.2], e: Pt = [q2[0] + off * 0.9, q2[1] + off * 0.2];
    ink.push(taper([a, lerpPt(a, e, 0.5), e], penProfile(W * 0.32, 0.3, 0.5, 0.05)));
  }
  if (o.ground) {
    ink.push(groundDash(r, L[0] + r.range(1, 3), L[0] - r.range(4, 9), base + r.jit(0.3), W * 0.6));
    ink.push(groundDash(r, R[0] - r.range(1, 3), R[0] + r.range(6, 12), base + r.jit(0.3), W * 0.65));
    const extra = r.int(1, 2);
    for (let i = 0; i < extra; i++) {
      const x0 = lerp(RB[0], R[0], r.range(0.05, 0.65));
      ink.push(groundDash(r, x0, x0 + r.range(4, 9), base + r.range(1.3, 2.6), W * 0.5));
    }
    if (r.chance(0.5)) {
      const x0 = lerp(L[0], S[0], r.range(0.2, 0.7));
      ink.push(groundDash(r, x0, x0 - r.range(3, 6), base + r.range(1.2, 2.2), W * 0.4));
    }
  }
  b.ink(ink);
}

function genMountain(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const peaks = p['peaks']! >= 1 ? Math.round(p['peaks']!) : r.pick([1, 1, 2, 2, 2, 3, 3]);
  const H = 76 * p['height']! * r.range(0.85, 1.1);
  const hw = H * r.range(0.5, 0.72) / p['steep']!;
  const snow = p['snow']!;
  const side = r.sign();
  const units: PeakOpts[] = [];
  if (peaks >= 2) units.push({ cx: side * hw * r.range(0.6, 0.95), base: -r.range(2, 5), H: H * r.range(0.5, 0.75), hw: hw * r.range(0.6, 0.8), snow: snow * 0.8, W: OUT * 0.85, ground: false });
  if (peaks >= 3) units.push({ cx: -side * hw * r.range(0.65, 1.0), base: -r.range(1, 4), H: H * r.range(0.45, 0.65), hw: hw * r.range(0.55, 0.75), snow: snow * 0.7, W: OUT * 0.85, ground: false });
  units.sort((a, c) => a.H - c.H);
  for (const u of units) peak(b, r, u);
  peak(b, r, { cx: 0, base: 0, H, hw, snow, W: OUT, ground: true });
  return b.finish([0, 0]);
}

function genVolcano(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const H = 62 * r.range(0.85, 1.1);
  const hw = H * r.range(0.85, 1.05);
  const cw = hw * r.range(0.22, 0.32);
  const CL: Pt = [-cw + r.jit(1), -H + r.jit(1.5)];
  const CR: Pt = [cw + r.jit(1), -H + r.jit(1.5)];
  const L: Pt = [-hw * r.range(0.9, 1.1), 0], R: Pt = [hw * r.range(0.95, 1.15), 0];
  const left = wobble(slope(r, CL, L, H), r, 0.3, 6);
  const right = wobble(slope(r, CR, R, H), r, 0.3, 6);
  const sil = left.slice().reverse().concat(right);
  const ccx = (CL[0] + CR[0]) / 2, ccy = (CL[1] + CR[1]) / 2, crx = (CR[0] - CL[0]) / 2, cry = crx * 0.3;
  const ang = Math.atan2(CR[1] - CL[1], CR[0] - CL[0]);
  const rim = ellipse(0, 0, crx, cry, 14).map(([x, y]) => [ccx + x * Math.cos(ang) - y * Math.sin(ang), ccy + x * Math.sin(ang) + y * Math.cos(ang)] as Pt);

  // smoke first so the cone occludes its base
  const puffs = Math.round(2 + p['smoke']! * 4);
  const drift = r.sign() * r.range(0.4, 1);
  const smokeInk: Pt[][] = [];
  const puffList: Array<{ c: Pt; rad: number }> = [];
  let pc: Pt = [ccx, ccy - 2];
  for (let i = 0; i < puffs; i++) {
    const rad = 3.5 + i * r.range(2.2, 3.2);
    pc = [pc[0] + drift * rad * r.range(0.6, 1.0) + r.jit(1), pc[1] - rad * r.range(0.8, 1.05)];
    puffList.push({ c: pc, rad });
  }
  for (let i = puffList.length - 1; i >= 0; i--) {
    const { c, rad } = puffList[i]!;
    const blob = lumpy(r, c[0], c[1], rad, rad * 0.8, r.int(4, 6), 0.18, -1);
    b.fill(blob, 'paper', true);
    const sh = shadeCrescent(blob, c[0], c[1], -rad * 0.35, -rad * 0.4, 0.75);
    if (sh.length) b.fill(sh, 'shade', true, 0.8);
    b.ink(loopStroke(blob, gapStart(blob, c[0], c[1], r.range(-2.6, -1.6)), r.range(0.08, 0.15), MID * 0.75));
    if (i > 0 && r.chance(0.7)) {
      const a = r.range(0.1, 0.4) * Math.PI;
      smokeInk.push(taper(arc(c[0], c[1], rad * 0.55, rad * 0.45, a, a + 1.4, 6), penProfile(FINE, 0.3, 0.3, 0.1)));
    }
  }
  if (smokeInk.length) b.ink(smokeInk);

  b.fill(sil, 'paper');
  // gullies radiating from the rim
  const g = r.int(4, 6);
  const gullies: Pt[][] = [];
  for (let i = 0; i < g; i++) {
    const t = (i + 0.5) / g;
    const s0 = lerpPt(CL, CR, clamp(t + r.jit(0.06), 0.05, 0.95));
    s0[1] += cry * 0.6;
    const footX = lerp(L[0], R[0], clamp(t + (t - 0.5) * 0.6 + r.jit(0.05), 0.05, 0.95));
    const e: Pt = [lerp(s0[0], footX, r.range(0.6, 0.9)), -H * r.range(0.04, 0.25)];
    const mid = lerpPt(s0, e, 0.5);
    mid[0] += r.jit(2);
    gullies.push(wobble([s0, mid, e], r, 0.4, 5));
  }
  const main = gullies[Math.floor(g / 2)]!;
  b.fill(main.concat(right.slice().reverse()), 'shade');
  // hatching on the shadow side between gullies, downhill
  const hatch: Pt[][] = [];
  const nh = r.int(8, 11);
  for (let i = 0; i < nh; i++) {
    const t = lerp(0.1, 0.92, (i + r.range(0.2, 0.8)) / nh);
    const A = pointAt(main, t), B = pointAt(right, clamp(t + 0.1, 0, 1));
    hatch.push([lerpPt(A, B, 0.1), lerpPt(A, B, r.range(0.4, 0.8))]);
  }
  b.lines(hatch, FINE, 'ink', 0.9);
  // crater
  b.fill(rim, 'shade');
  const inner = rim.map(([x, y]) => [lerp(ccx, x, 0.8), lerp(ccy, y, 0.7) - cry * 0.15] as Pt);
  b.ink(inner, true, 0.85);
  const ink: Pt[][] = [];
  ink.push(taper(sil, (t) => OUT * penProfile(1, 0.18, 0.16, 0.04)(t) * (t > 0.5 ? 1.15 : 0.85)));
  ink.push(taper(arc(ccx, ccy, crx, cry, 0.1, Math.PI - 0.1, 9).map(([x, y]) => [x, y + (x - ccx) * Math.tan(ang)] as Pt), penProfile(MID, 0.2, 0.2, 0.2)));
  for (let i = 0; i < gullies.length; i++) {
    const w = i >= Math.floor(g / 2) ? MID * 1.1 : MID * 0.7;
    ink.push(taper(gullies[i]!, penProfile(w, 0.05, 0.7, 0.05)));
  }
  ink.push(groundDash(r, L[0] + 2, L[0] - r.range(4, 9), r.jit(0.3), OUT * 0.6));
  ink.push(groundDash(r, R[0] - 2, R[0] + r.range(6, 12), r.jit(0.3), OUT * 0.6));
  const x0 = lerp(0, R[0], r.range(0.2, 0.6));
  ink.push(groundDash(r, x0, x0 + r.range(4, 9), r.range(1.3, 2.4), OUT * 0.5));
  b.ink(ink);
  return b.finish([0, 0]);
}

// ================================================================ hills

function genHill(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const w = r.range(40, 58) * p['width']!;
  const h = r.range(13, 19);
  const peakT = clamp(0.5 + r.jit(0.14), 0.3, 0.7);
  const a = Math.log(0.5) / Math.log(peakT);
  const n = 12;
  const noise = smoothNoise(r, n + 1, 1);
  const dome: Pt[] = [];
  for (let i = 0; i <= n; i++) {
    const t = i / n;
    const u = Math.pow(t, a);
    const y = -h * Math.pow(Math.sin(Math.PI * u), r.range(0.75, 0.95)) + (i > 0 && i < n ? noise[i]! * 0.5 : 0);
    dome.push([lerp(-w / 2, w / 2, t), y]);
  }
  const d = wobble(dome, r, 0.25, 5);
  b.fill(d, 'paper');
  // shadow side: crescent between the right half of the dome and an inner curve
  const ip = Math.round(peakT * (d.length - 1));
  const shadeOuter = d.slice(ip);
  const shadeInner = shadeOuter.map(([x, y], i) => {
    const k = i / (shadeOuter.length - 1);
    return [x - (1 - k) * w * 0.12, y * lerp(0.35, 1, k * k)] as Pt;
  });
  b.fill(shadeOuter.concat(shadeInner.reverse()), 'shade', true, 0.85);
  // contour strokes
  const ink: Pt[][] = [];
  const contours = r.int(1, 2);
  for (let c = 0; c < contours; c++) {
    const k = 0.55 - c * 0.2 + r.jit(0.05);
    const t0 = r.range(0.2, 0.35), t1 = r.range(0.6, 0.85);
    const seg: Pt[] = [];
    for (let i = 0; i < 6; i++) {
      const q = pointAt(d, lerp(t0, t1, i / 5));
      seg.push([q[0] * (0.85 + c * 0.05), q[1] * k]);
    }
    ink.push(taper(seg, penProfile(MID * 0.6, 0.35, 0.35, 0.05)));
  }
  // short shadow hatch on the right foot
  const hatch: Pt[][] = [];
  const nh = r.int(3, 5);
  for (let i = 0; i < nh; i++) {
    const q = pointAt(d, lerp(0.62, 0.9, (i + 0.5) / nh));
    const len = Math.min(4, -q[1] * 0.6);
    hatch.push([[q[0] - 0.6, q[1] + 1.2], [q[0] - 1.6, q[1] + 1.2 + len]]);
  }
  b.lines(hatch, FINE, 'ink', 0.85);
  ink.push(taper(d, penProfile(OUT * 0.9, 0.22, 0.18, 0.04)));
  ink.push(groundDash(r, -w / 2 + 2, -w / 2 - r.range(3, 7), 0.2, OUT * 0.5));
  ink.push(groundDash(r, w / 2 - 2, w / 2 + r.range(4, 9), 0.2, OUT * 0.55));
  b.ink(ink);
  return b.finish([0, 0]);
}

// ================================================================ trees & vegetation

/** Scalloped blob (tree crowns, bushes, smoke): cusps between lumps. */
function lumpy(r: Rng, cx: number, cy: number, rx: number, ry: number, lumps: number, amp: number, flatBottom: number): Pt[] {
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
function gapStart(pts: readonly Pt[], cx: number, cy: number, angle = -2.2): number {
  let best = 0, bd = Infinity;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i]!;
    let d = Math.abs(Math.atan2(p[1] - cy, p[0] - cx) - angle);
    if (d > Math.PI) d = Math.PI * 2 - d;
    if (d < bd) { bd = d; best = i; }
  }
  return best / pts.length;
}

function shadeCrescent(pts: readonly Pt[], cx: number, cy: number, ox: number, oy: number, k: number): Pt[] {
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

function genConifer(r: Rng): Stamp {
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

function genDeciduous(r: Rng): Stamp {
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

function genPalm(r: Rng): Stamp {
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

function genDeadTree(r: Rng): Stamp {
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

function genShrub(r: Rng): Stamp {
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

function blade(r: Rng, x: number, h: number, ang: number, w: number): Pt[] {
  const e: Pt = [x + Math.cos(ang) * h, Math.sin(ang) * h];
  const c: Pt = [lerp(x, e[0], 0.5) + r.jit(h * 0.1), lerp(0, e[1], 0.5)];
  return taper(quad([x, 0], c, e, 5), penProfile(w, 0, 0.9, 0.05));
}

function genGrassTuft(r: Rng): Stamp {
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

function genReeds(r: Rng): Stamp {
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

function genDunes(r: Rng): Stamp {
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

function genCactus(r: Rng): Stamp {
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

function drawBoulder(b: InkBuilder, r: Rng, cx: number, y: number, rad: number, w = MID): void {
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

function genRock(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(2, 4);
  const list: Array<{ x: number; y: number; rad: number }> = [];
  for (let i = 0; i < n; i++) list.push({ x: (i - (n - 1) / 2) * 7 + r.jit(2), y: r.range(-2, 1.5), rad: i === 0 ? r.range(5, 7) : r.range(2.5, 5) });
  list.sort((a, c) => a.y - c.y);
  for (const s of list) drawBoulder(b, r, s.x, s.y, s.rad);
  b.ink(groundDash(r, -n * 4 - 3, n * 4 + 3, 2, MID * 0.6));
  return b.finish([0, 0]);
}

function genCrevasse(r: Rng): Stamp {
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

// ================================================================ architecture

/** A small house, front-left corner at (x, y) on the ground. s ~ 1 => ~7 units wide. */
function house(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const ink: Pt[][] = [];
  if (r.chance(0.45)) {
    // gable end toward the viewer
    const w = s * r.range(4.5, 6), h = s * r.range(3, 4.2), rh = s * r.range(2.4, 3.6);
    const dx = s * r.range(3.5, 5.5), dy = dx * 0.45;
    const A: Pt = [x + w / 2, y - h - rh], E: Pt = [x + w + 0.4, y - h + 0.3];
    b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
    b.outlined([[x, y], [x, y - h], A, [x + w, y - h], [x + w, y]], 'wall', ARCH);
    b.outlined([A, E, [E[0] + dx, E[1] - dy], [A[0] + dx, A[1] - dy]], 'roof', ARCH);
    b.lines([[[A[0] + dx * 0.33, A[1] - dy * 0.33 + 0.4], [E[0] + dx * 0.33 - 0.3, E[1] - dy * 0.33]], [[A[0] + dx * 0.66, A[1] - dy * 0.66 + 0.4], [E[0] + dx * 0.66 - 0.3, E[1] - dy * 0.66]]], FINE * 0.7, 'ink', 0.7);
    ink.push([[x + w * 0.38, y], [x + w * 0.38, y - h * 0.55], [x + w * 0.62, y - h * 0.55], [x + w * 0.62, y]]);
  } else {
    // long side toward the viewer
    const w = s * r.range(6, 9), h = s * r.range(2.8, 3.8), rh = s * r.range(3, 4.2);
    const dx = s * r.range(2.5, 4), dy = dx * 0.45;
    const R1: Pt = [x + dx * 0.5, y - h - rh], R2: Pt = [x + w + dx * 0.5, y - h - rh];
    b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
    b.outlined([[x + w, y - h], [x + w + dx, y - h - dy], R2], 'wall', ARCH);
    b.outlined([[x, y], [x, y - h], [x + w, y - h], [x + w, y]], 'wall', ARCH);
    b.outlined([[x - 0.4, y - h + 0.3], [x + w + 0.3, y - h + 0.3], R2, R1], 'roof', ARCH);
    const tiles: Pt[][] = [];
    const nt = Math.max(2, Math.round(w / 2.4));
    for (let i = 1; i < nt; i++) { const t = i / nt; tiles.push([[lerp(x, x + w, t), y - h], [lerp(R1[0], R2[0], t), R1[1] + 0.4]]); }
    b.lines(tiles, FINE * 0.7, 'ink', 0.6);
    const dxp = x + w * r.range(0.2, 0.6);
    ink.push([[dxp, y], [dxp, y - h * 0.6], [dxp + s * 1.2, y - h * 0.6], [dxp + s * 1.2, y]]);
    if (r.chance(0.6)) {
      const wx = dxp > x + w / 2 ? x + w * 0.2 : x + w * 0.75;
      ink.push([[wx, y - h * 0.7], [wx + s * 0.9, y - h * 0.7], [wx + s * 0.9, y - h * 0.4], [wx, y - h * 0.4]]);
    }
    if (r.chance(0.5)) {
      const cxp = lerp(R1[0], R2[0], r.range(0.15, 0.85));
      b.outlined([[cxp, R1[1] + 0.6], [cxp, R1[1] - s * 1.6], [cxp + s * 1.1, R1[1] - s * 1.6], [cxp + s * 1.1, R1[1] + 0.6]], 'wall', ARCH);
    }
  }
  b.ink(ink, false);
}

interface TowerOpts { roof: 'cone' | 'crenel' | 'dome'; flag?: boolean; windows?: number; }

/** Round tower standing on (cx, y). */
function tower(b: InkBuilder, r: Rng, cx: number, y: number, w: number, h: number, o: TowerOpts): void {
  const hw = w / 2, e = w * 0.16;
  // left-bottom -> right-bottom along the front (lower) half of the base ellipse
  const bottom = arc(cx, y, hw, e, Math.PI, 0, 7).map(([px, py]) => [px, 2 * y - py] as Pt);
  const body: Pt[] = [[cx - hw, y - h], ...bottom, [cx + hw, y - h]];
  b.outlined(body, 'wall', ARCH);
  const sx = cx + hw * 0.3;
  b.fill([[sx, y - h], [sx, y + e * 0.95], ...bottom.filter((p) => p[0] > sx), [cx + hw, y - h]], 'shade', false);
  b.lines([body], ARCH, 'ink');
  const ink: Pt[][] = [];
  const nw = o.windows ?? (h > w * 1.6 ? 2 : 1);
  for (let i = 0; i < nw; i++) {
    const wy = y - h * (0.35 + 0.35 * i) + r.jit(h * 0.04);
    const wx = cx - hw * 0.25 + r.jit(hw * 0.15);
    ink.push([[wx - w * 0.07, wy], [wx - w * 0.07, wy - w * 0.3], [wx, wy - w * 0.38], [wx + w * 0.07, wy - w * 0.3], [wx + w * 0.07, wy]]);
  }
  if (ink.length) b.ink(ink, false);
  let topY = y - h;
  if (o.roof === 'cone') {
    const rx = hw * 1.18, ap: Pt = [cx + r.jit(0.3), y - h - w * r.range(1.1, 1.6)];
    const eave = arc(cx, y - h, rx, e * 1.2, 0, Math.PI, 7);
    const cone: Pt[] = [ap, [cx + rx, y - h], ...eave, [cx - rx, y - h]];
    b.outlined(cone, 'roof', ARCH);
    b.fill([ap, [cx + rx, y - h], ...eave.filter((p) => p[0] > cx + rx * 0.15), [cx + rx * 0.15, y - h + e * 1.2]], 'ink', false, 0.25);
    topY = ap[1];
  } else if (o.roof === 'dome') {
    const dome = arc(cx, y - h, hw * 1.05, w * 0.7, Math.PI, Math.PI * 2, 9);
    b.outlined(dome, 'roof', ARCH, true);
    b.fill(arc(cx, y - h, hw * 1.05, w * 0.7, Math.PI * 1.6, Math.PI * 2, 5).concat([[cx + hw * 0.3, y - h]]), 'ink', true, 0.22);
    topY = y - h - w * 0.7;
  } else {
    const cw = hw * 1.18, ch = w * 0.32;
    const band: Pt[] = [[cx - cw, y - h - ch], [cx - cw, y - h], [cx + cw, y - h], [cx + cw, y - h - ch]];
    b.outlined(band, 'wall', ARCH);
    b.fill([[cx + cw * 0.35, y - h - ch], [cx + cw * 0.35, y - h], [cx + cw, y - h], [cx + cw, y - h - ch]], 'shade', false);
    const merlons: Pt[][] = [];
    const nm = Math.max(3, Math.round(w / 2.2)) | 1;
    for (let i = 0; i < nm; i += 2) {
      const x0 = lerp(cx - cw, cx + cw, i / nm), x1 = lerp(cx - cw, cx + cw, (i + 1) / nm);
      merlons.push([[x0, y - h - ch], [x0, y - h - ch - ch * 0.75], [x1, y - h - ch - ch * 0.75], [x1, y - h - ch]]);
    }
    b.outlined(merlons, 'wall', ARCH);
    b.lines([band], ARCH, 'ink');
    topY = y - h - ch * 1.75;
  }
  if (o.flag) flag(b, r, cx, topY, w * 0.9);
}

function flag(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const top = y - s * 1.3;
  const dir = r.sign();
  b.lines([[[x, y + 0.5], [x, top]]], 0.55, 'ink');
  const fl = s * r.range(0.9, 1.3);
  const wave = r.range(0.2, 0.5);
  const p: Pt[] = [[x, top], [x + dir * fl * 0.5, top - wave], [x + dir * fl, top + s * 0.18], [x + dir * fl * 0.8, top + s * 0.25], [x + dir * fl, top + s * 0.4], [x + dir * fl * 0.5, top + s * 0.45 - wave], [x, top + s * 0.45]];
  b.outlined(p, 'roof', 0.45, false);
}

/** Wall band along an ellipse arc (plan ellipse rx, ry around (cx, cy)), crenellated top. */
function wallArc(b: InkBuilder, r: Rng, cx: number, cy: number, rx: number, ry: number, a0: number, a1: number, wh: number, shade: boolean): void {
  const n = Math.max(4, Math.round((Math.abs(a1 - a0) * rx) / 3));
  const ground = arc(cx, cy, rx, ry, a0, a1, n);
  const top = ground.map(([x, y]) => [x, y - wh] as Pt);
  // crenellations as a square wave along the top
  const crown: Pt[] = [];
  const m = wh * 0.35;
  const steps = Math.max(4, Math.round(polyLength(top) / 2.2));
  for (let i = 0; i <= steps; i++) {
    const p = pointAt(top, i / steps);
    const up = i % 2 === 1;
    crown.push([p[0], p[1]]);
    if (i < steps) {
      const q = pointAt(top, (i + 1) / steps);
      crown.push([p[0], p[1] - (up ? m : 0)], [q[0], q[1] - (up ? m : 0)]);
    }
  }
  b.fill(ground.concat(crown.slice().reverse()), 'wall', false);
  if (shade) {
    // the face turned away from the light (right-hand, front-facing part)
    const seg: Pt[] = [];
    for (const p of ground) if (p[0] > cx + rx * 0.45) seg.push(p);
    if (seg.length >= 2) b.fill(seg.concat(seg.map(([x, y]) => [x, y - wh] as Pt).reverse()), 'shade', false);
  }
  b.lines([jitter(ground, r, 0.15), crown], ARCH, 'ink');
  const courses: Pt[][] = [ground.map(([x, y]) => [x, y - wh * 0.5] as Pt)];
  b.lines(courses, FINE * 0.6, 'ink', 0.5);
  b.lines([[ground[0]!, top[0]!], [ground[n - 1]!, top[n - 1]!]], ARCH, 'ink');
}

function scatter(r: Rng, n: number, rx: number, ry: number, minD: number, cx = 0, cy = 0): Pt[] {
  const out: Pt[] = [];
  let tries = 0;
  while (out.length < n && tries < n * 30) {
    tries++;
    const a = r.range(0, Math.PI * 2), d = Math.sqrt(r.next());
    const p: Pt = [cx + Math.cos(a) * d * rx, cy + Math.sin(a) * d * ry];
    if (out.every((q) => Math.hypot((q[0] - p[0]) * 0.8, (q[1] - p[1]) * 1.6) > minD)) out.push(p);
  }
  return out;
}

function genVillage(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const n = p['houses']! >= 1 ? Math.round(p['houses']!) : r.int(2, 4);
  const pos = scatter(r, n, 10, 4, 7);
  pos.sort((a, c) => a[1] - c[1]);
  const extras: Array<{ y: number; draw: () => void }> = [];
  if (r.chance(0.55)) {
    const tx = r.sign() * r.range(10, 15), ty = r.range(-3, 2);
    extras.push({ y: ty, draw: () => smallTree(b, r, tx, ty, 0.7) });
  }
  const items = pos.map((q) => ({ y: q[1], draw: () => house(b, r, q[0] - 4, q[1], r.range(1.25, 1.5)) })).concat(extras);
  items.sort((a, c) => a.y - c.y);
  for (const it of items) it.draw();
  const ink: Pt[][] = [];
  ink.push(groundDash(r, -14, -4, 4.2, MID * 0.6));
  ink.push(groundDash(r, 2, 15, 4.6, MID * 0.6));
  b.ink(ink);
  if (r.chance(0.6)) {
    const path = wobble([[r.jit(3), 5], [r.range(-6, 6), 10], [r.range(-10, 10), 15]], r, 0.6, 3);
    b.lines([path, translate(path, 2.2, 0)], FINE * 0.8, 'ink', 0.65, true);
  }
  return b.finish([0, 0]);
}

function smallTree(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const rx = 4.2 * s, ry = 3.8 * s;
  b.ink(taper([[x, y], [x, y - 5 * s]], penProfile(1.3 * s, 0, 0.5, 0.4)));
  const crown = lumpy(r, x, y - 5 * s - ry * 0.6, rx, ry, r.int(5, 7), 0.18, 0.8);
  b.fill(crown, 'foliage', false);
  const sh = shadeCrescent(crown, x, y - 5 * s - ry * 0.6, -rx * 0.3, -ry * 0.35, 0.7);
  if (sh.length) b.fill(sh, 'foliageDark');
  b.ink(loopStroke(crown, gapStart(crown, x, y - 5 * s - ry * 0.6), 0.08, MID * 0.9));
}

function church(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  // nave (long house) + a square tower with a spire at its front-left
  const w = s * 8, h = s * 4.2, dx = s * 3.2, dy = dx * 0.45, rh = s * 3.6;
  const tx = x - s * 1.2;
  const R1: Pt = [x + dx * 0.5, y - h - rh], R2: Pt = [x + w + dx * 0.5, y - h - rh];
  b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
  b.outlined([[x + w, y - h], [x + w + dx, y - h - dy], R2], 'wall', ARCH);
  b.outlined([[x, y], [x, y - h], [x + w, y - h], [x + w, y]], 'wall', ARCH);
  b.outlined([[x - 0.3, y - h + 0.3], [x + w + 0.3, y - h + 0.3], R2, R1], 'roof', ARCH);
  const win: Pt[][] = [];
  for (let i = 0; i < 3; i++) { const wx = x + w * (0.35 + i * 0.2); win.push([[wx, y - h * 0.25], [wx, y - h * 0.65], [wx + 0.5 * s, y - h * 0.75], [wx + s, y - h * 0.65], [wx + s, y - h * 0.25]]); }
  b.ink(win, false);
  const tw = s * 3.6, th = s * 11;
  const tdx = s * 1.6, tdy = tdx * 0.45;
  b.outlined([[tx + tw, y], [tx + tw, y - th], [tx + tw + tdx, y - th - tdy], [tx + tw + tdx, y - tdy]], 'shade', ARCH);
  b.outlined([[tx, y], [tx, y - th], [tx + tw, y - th], [tx + tw, y]], 'wall', ARCH);
  const ap: Pt = [tx + tw * 0.5 + tdx * 0.5, y - th - s * r.range(7, 10)];
  b.outlined([[tx - 0.3, y - th], [tx + tw, y - th], ap], 'roof', ARCH);
  b.outlined([[tx + tw, y - th], [tx + tw + tdx, y - th - tdy], ap], 'roof', ARCH);
  b.fill([[tx + tw, y - th], [tx + tw + tdx, y - th - tdy], ap], 'ink', false, 0.3);
  b.ink([[[tx + tw * 0.35, y], [tx + tw * 0.35, y - th * 0.22], [tx + tw * 0.5, y - th * 0.27], [tx + tw * 0.65, y - th * 0.22], [tx + tw * 0.65, y]],
    [[tx + tw * 0.42, y - th * 0.6], [tx + tw * 0.42, y - th * 0.75], [tx + tw * 0.58, y - th * 0.75], [tx + tw * 0.58, y - th * 0.6]]], false);
  b.lines([[ap, [ap[0], ap[1] - s * 2]], [[ap[0] - s * 0.7, ap[1] - s * 1.3], [ap[0] + s * 0.7, ap[1] - s * 1.3]]], 0.5, 'ink');
}

function genTown(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(5, 8);
  const pos = scatter(r, n, 17, 7, 7.5);
  const cpos: Pt = [r.jit(4), -r.range(3, 6)];
  const items: Array<{ y: number; draw: () => void }> = pos
    .filter((q) => Math.hypot(q[0] - cpos[0], (q[1] - cpos[1]) * 2) > 9)
    .map((q) => ({ y: q[1], draw: () => house(b, r, q[0] - 4, q[1], r.range(1.15, 1.4)) }));
  items.push({ y: cpos[1], draw: () => church(b, r, cpos[0] - 4, cpos[1], 1.1) });
  items.sort((a, c) => a.y - c.y);
  b.fill(ellipse(0, 1, 21, 9, 14), 'paper');
  for (const it of items) it.draw();
  // partial wall on the front, with a tower
  const a0 = r.range(0.05, 0.5) * Math.PI, a1 = a0 + r.range(0.35, 0.55) * Math.PI;
  wallArc(b, r, 0, 0, 24, 11, a0, a1, 3.4, true);
  const tp = arc(0, 0, 24, 11, a1, a1, 1)[0]!;
  tower(b, r, tp[0], tp[1] + 0.5, 4.5, 7, { roof: r.chance(0.5) ? 'cone' : 'crenel', windows: 1 });
  if (r.chance(0.5)) {
    const tq = arc(0, 0, 24, 11, a0, a0, 1)[0]!;
    tower(b, r, tq[0], tq[1] + 0.5, 4.2, 6.4, { roof: 'crenel', windows: 1 });
  }
  return b.finish([0, 0]);
}

function keep(b: InkBuilder, r: Rng, x: number, y: number, w: number, h: number, capital: boolean): void {
  // square keep in oblique view with corner turrets
  const dx = w * 0.45, dy = dx * 0.45;
  b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
  b.outlined([[x, y], [x, y - h], [x + w, y - h], [x + w, y]], 'wall', ARCH);
  b.outlined([[x, y - h], [x + w, y - h], [x + w + dx, y - h - dy], [x + dx, y - h - dy]], 'shade', ARCH);
  const ink: Pt[][] = [];
  const nw = Math.max(2, Math.round(w / 3.5));
  for (let i = 0; i < nw; i++) {
    const wx = x + w * (i + 0.5) / nw - 0.4;
    ink.push([[wx, y - h * 0.45], [wx, y - h * 0.62], [wx + 0.8, y - h * 0.62], [wx + 0.8, y - h * 0.45]]);
  }
  ink.push([[x + w * 0.4, y], [x + w * 0.4, y - h * 0.2], [x + w * 0.5, y - h * 0.26], [x + w * 0.6, y - h * 0.2], [x + w * 0.6, y]]);
  b.ink(ink, false);
  const tw = w * 0.32;
  const roof: TowerOpts['roof'] = capital ? 'cone' : r.pick(['crenel', 'cone'] as const);
  tower(b, r, x + dx, y - h - dy + 0.5, tw * 0.9, h * 0.25, { roof, windows: 0 });
  tower(b, r, x + w + dx, y - h - dy + 0.5, tw * 0.9, h * 0.25, { roof, windows: 0, flag: capital });
  tower(b, r, x, y - h + 0.5, tw, h * 0.3, { roof, windows: 1, flag: !capital && r.chance(0.6) });
  tower(b, r, x + w, y - h + 0.5, tw, h * 0.3, { roof, windows: 1 });
  if (capital) tower(b, r, x + w * 0.5 + dx * 0.5, y - h - dy * 0.5, tw * 1.3, h * 0.55, { roof: r.chance(0.5) ? 'dome' : 'cone', windows: 1, flag: true });
}

function walledCity(r: Rng, capital: boolean): Stamp {
  const b = new InkBuilder();
  const R = capital ? r.range(46, 54) : r.range(30, 38);
  const ry = R * r.range(0.4, 0.46);
  const wh = capital ? 6.5 : 5;
  b.fill(ellipse(0, 0, R + 1, ry + 1, 20).map(([x, y]) => [x, y < 0 ? y - wh : y] as Pt), 'paper');
  // back wall and back towers
  wallArc(b, r, 0, 0, R, ry, Math.PI, Math.PI * 2, wh, false);
  for (const a of [1.25, 1.5, 1.75]) {
    if (a === 1.5 && !capital) continue;
    const tp = arc(0, 0, R, ry, a * Math.PI, a * Math.PI, 1)[0]!;
    tower(b, r, tp[0], tp[1] + 0.5, wh * 1.0, wh * 1.5, { roof: 'crenel', windows: 0 });
  }
  if (capital) {
    // inner citadel wall
    wallArc(b, r, 0, -ry * 0.3, R * 0.42, ry * 0.42, Math.PI, Math.PI * 2, wh * 0.8, false);
  }
  const nh = capital ? r.int(20, 26) : r.int(11, 15);
  const pos = scatter(r, nh, R * 0.86, ry * 0.8, capital ? 6 : 6.2, 0, -1);
  const kp: Pt = capital ? [r.jit(4) - 6, -ry * 0.35] : [r.jit(R * 0.25) - 4, -ry * r.range(0.1, 0.35)];
  const kw = capital ? 14 : 9;
  const items: Array<{ y: number; draw: () => void }> = pos
    .filter((q) => !(q[0] > kp[0] - 6 && q[0] < kp[0] + kw + 6 && Math.abs(q[1] - kp[1]) < (capital ? 8 : 5)))
    .map((q) => ({ y: q[1], draw: () => house(b, r, q[0] - 3.5, q[1], r.range(0.95, 1.15)) }));
  items.push({ y: kp[1], draw: () => keep(b, r, kp[0], kp[1], kw, capital ? 16 : 11, capital) });
  if (r.chance(capital ? 1 : 0.6)) {
    const cp = scatter(r, 1, R * 0.5, ry * 0.4, 0, R * 0.25 * r.sign(), 0)[0] ?? [R * 0.3, 0];
    items.push({ y: cp[1] + 0.01, draw: () => church(b, r, cp[0], cp[1], 0.9) });
  }
  if (capital) {
    // inner wall front side drawn in the keep's row
    items.push({ y: kp[1] + 0.5, draw: () => wallArc(b, r, 0, -ry * 0.3, R * 0.42, ry * 0.42, 0, Math.PI, wh * 0.8, true) });
  }
  items.sort((a, c) => a.y - c.y);
  for (const it of items) it.draw();
  // front wall
  wallArc(b, r, 0, 0, R, ry, 0, Math.PI, wh, true);
  // gate
  const gx = r.jit(R * 0.15), gy = ry * Math.sqrt(1 - (gx / R) ** 2);
  b.ink([[gx - 1.6, gy + 0.3], [gx - 1.6, gy - wh * 0.6], [gx, gy - wh * 0.8], [gx + 1.6, gy - wh * 0.6], [gx + 1.6, gy + 0.3]], true);
  const tset = capital ? [0, 0.2, 0.4, 0.6, 0.8, 1] : [0, 0.28, 0.72, 1];
  for (const t of tset) {
    const a = t * Math.PI;
    const tp = arc(0, 0, R, ry, a, a, 1)[0]!;
    tower(b, r, tp[0], tp[1] + 0.5, wh * 1.25, wh * 1.9, { roof: capital && (t === 0 || t === 1) ? 'cone' : 'crenel', windows: 1, flag: capital && t === 0.4 });
  }
  for (const s of [-1, 1]) tower(b, r, gx + s * 2.8, gy + 0.5, wh * 0.9, wh * 1.6, { roof: 'crenel', windows: 0 });
  const ink: Pt[][] = [];
  ink.push(groundDash(r, -R - 6, -R * 0.6, ry * 0.75 + 1.5, MID * 0.7));
  ink.push(groundDash(r, R * 0.55, R + 8, ry * 0.8 + 1.5, MID * 0.8));
  b.ink(ink);
  return b.finish([0, 0]);
}

function genCastle(r: Rng): Stamp {
  const b = new InkBuilder();
  const R = r.range(18, 24), ry = R * 0.42, wh = 5;
  // mound
  const mound: Pt[] = [];
  for (let i = 0; i <= 10; i++) { const t = i / 10; mound.push([lerp(-R - 9, R + 9, t), ry + 3 - Math.pow(Math.sin(Math.PI * t), 0.6) * (ry + 6)]); }
  b.fill(mound, 'paper');
  b.fill(mound.slice(6).concat(mound.slice(6).map(([x, y]) => [x - 3, lerp(y, ry + 3, 0.6)] as Pt).reverse()), 'shade', true, 0.8);
  b.ink(taper(mound, penProfile(OUT * 0.8, 0.25, 0.2, 0.05)));
  wallArc(b, r, 0, 0, R, ry, Math.PI, Math.PI * 2, wh, false);
  const kx = r.jit(4) - 6;
  keep(b, r, kx, -ry * 0.15, r.range(10, 13), r.range(15, 20), false);
  wallArc(b, r, 0, 0, R, ry, 0, Math.PI, wh, true);
  const nt = r.int(2, 4);
  const ts = nt === 2 ? [0, 1] : nt === 3 ? [0, 0.5, 1] : [0, 0.33, 0.67, 1];
  for (const t of ts) {
    const tp = arc(0, 0, R, ry, t * Math.PI, t * Math.PI, 1)[0]!;
    tower(b, r, tp[0], tp[1] + 0.5, 6, 10, { roof: r.chance(0.4) ? 'cone' : 'crenel', windows: 1, flag: r.chance(0.25) });
  }
  if (nt !== 3) b.ink([[-1.6, ry + 0.3], [-1.6, ry - 3], [0, ry - 4], [1.6, ry - 3], [1.6, ry + 0.3]], true);
  b.ink(groundDash(r, R * 0.4, R + 12, ry + 3.4, MID * 0.7));
  return b.finish([0, 0]);
}

function genTower(r: Rng): Stamp {
  const b = new InkBuilder();
  const w = r.range(7, 9), h = r.range(20, 28);
  const rocks = r.chance(0.6);
  if (rocks) { drawBoulder(b, r, -w * 0.75, 1.5, r.range(2.5, 3.5), MID); }
  tower(b, r, 0, 0, w, h, { roof: r.pick(['cone', 'crenel', 'cone'] as const), windows: r.int(2, 3), flag: r.chance(0.5) });
  b.ink([[-1, 0.9], [-1, -2.6], [0, -3.4], [1, -2.6], [1, 0.9]], true);
  if (rocks) drawBoulder(b, r, w * 0.7, 2, r.range(2, 3), MID);
  b.ink([groundDash(r, -w - 4, -w * 0.4, 1.5, MID * 0.7), groundDash(r, w * 0.4, w + 6, 1.8, MID * 0.8)]);
  return b.finish([0, 0]);
}

function genRuins(r: Rng): Stamp {
  const b = new InkBuilder();
  const n = r.int(2, 4);
  const pieces: Array<{ x: number; y: number; w: number; h: number; kind: number }> = [];
  for (let i = 0; i < n; i++) pieces.push({ x: (i - n / 2) * 8 + r.jit(2), y: r.range(-3, 2), w: r.range(4, 9), h: r.range(5, 13), kind: r.int(0, 3) });
  pieces.sort((a, c) => a.y - c.y);
  for (const p of pieces) {
    if (p.kind === 3) {
      // broken column
      const cw = 2.6;
      const top: Pt[] = [[p.x + cw, p.y - p.h * 0.8], [p.x + cw * 0.6, p.y - p.h * 0.8 - 1.2], [p.x + cw * 0.3, p.y - p.h * 0.8 + 0.3], [p.x, p.y - p.h * 0.8 - 0.6]];
      b.outlined([[p.x, p.y], [p.x + cw, p.y], ...top], 'wall', ARCH);
      b.fill([[p.x + cw * 0.6, p.y], [p.x + cw, p.y], [p.x + cw, p.y - p.h * 0.8], [p.x + cw * 0.6, p.y - p.h * 0.8 - 1.2]], 'shade', false);
      b.outlined([[p.x - 0.6, p.y], [p.x + cw + 0.6, p.y], [p.x + cw + 0.6, p.y - 1], [p.x - 0.6, p.y - 1]], 'wall', ARCH);
      b.lines([[[p.x + cw * 0.33, p.y - 1], [p.x + cw * 0.33, p.y - p.h * 0.75]]], FINE * 0.6, 'ink', 0.6);
      continue;
    }
    // wall piece with a jagged broken top
    const top: Pt[] = [];
    const k = r.int(3, 5);
    for (let i = 0; i <= k; i++) top.push([p.x + p.w - (p.w * i) / k, p.y - p.h * (i === 0 || i === k ? r.range(0.35, 0.7) : r.range(0.6, 1))]);
    const d = 2.2, dy = 1;
    b.outlined([[p.x + p.w, p.y], [p.x + p.w + d, p.y - dy], [p.x + p.w + d, top[0]![1] - dy], [p.x + p.w, top[0]![1]]], 'shade', ARCH);
    b.outlined([[p.x, p.y], [p.x + p.w, p.y], ...top], 'wall', ARCH);
    const courses: Pt[][] = [];
    for (let y = p.y - 2; y > p.y - p.h * 0.6; y -= 2.2) {
      courses.push([[p.x + 0.3, y], [p.x + p.w - 0.3, y]]);
      const jx = p.x + r.range(0.2, 0.8) * p.w;
      courses.push([[jx, y], [jx, y + 2.2]]);
    }
    b.lines(courses, FINE * 0.6, 'ink', 0.55);
    if (p.kind === 1 && p.w > 5) {
      const ax = p.x + p.w * 0.5, ah = Math.min(p.h * 0.45, 5);
      b.ink([[ax - 1.3, p.y], [ax - 1.3, p.y - ah * 0.6], ...arc(ax, p.y - ah * 0.6, 1.3, 1.3, Math.PI, Math.PI * 2, 5), [ax + 1.3, p.y]], false, 0.9);
    }
  }
  const rubble: Pt[][] = [];
  for (let i = 0; i < r.int(3, 6); i++) {
    const x = r.range(-n * 5, n * 5), y = r.range(1, 3.5), s = r.range(0.8, 1.6), a = r.jit(0.5);
    rubble.push([[0, 0], [s * 1.6, 0], [s * 1.6, -s], [0, -s]].map(([px, py]) => [x + px * Math.cos(a) - py * Math.sin(a), y + px * Math.sin(a) + py * Math.cos(a)] as Pt));
  }
  b.outlined(rubble, 'wall', ARCH);
  const ink: Pt[][] = [groundDash(r, -n * 5 - 4, n * 5 + 4, 3.8, MID * 0.7)];
  for (let i = 0; i < 2; i++) {
    const gx = r.range(-n * 4, n * 4);
    for (let j = 0; j < 3; j++) ink.push(blade(r, gx + j * 0.8 - 0.8, r.range(2, 3.5), -Math.PI / 2 + (j - 1) * 0.5, 0.5).map(([x, y]) => [x, y + 3] as Pt));
  }
  b.ink(ink);
  return b.finish([0, 0]);
}

function genTemple(r: Rng): Stamp {
  const b = new InkBuilder();
  const cols = r.int(4, 6);
  const W = cols * r.range(4.5, 5.5);
  const ch = r.range(11, 15);
  const steps = r.int(2, 3);
  const dx = r.range(3, 5), dy = dx * 0.45;
  let y = 0;
  for (let i = 0; i < steps; i++) {
    const inset = (steps - 1 - i) * 1.4;
    const sw = W + 4 + inset * 2 - (steps - 1) * 2.8;
    const x0 = -sw / 2;
    b.outlined([[x0 + sw, y], [x0 + sw + dx, y - dy], [x0 + sw + dx, y - 1.4 - dy], [x0 + sw, y - 1.4]], 'shade', ARCH);
    b.outlined([[x0, y], [x0 + sw, y], [x0 + sw, y - 1.4], [x0, y - 1.4]], 'wall', ARCH);
    y -= 1.4;
  }
  void W;
  const top = y;
  // dark cella behind the colonnade
  b.outlined([[W / 2, top], [W / 2 + dx, top - dy], [W / 2 + dx, top - ch - dy], [W / 2, top - ch]], 'shade', ARCH);
  b.fill([[-W / 2 + 1, top], [W / 2 - 1, top], [W / 2 - 1, top - ch], [-W / 2 + 1, top - ch]], 'ink', false, 0.78);
  const colW = 2.1;
  for (let i = 0; i < cols; i++) {
    const cx = lerp(-W / 2 + colW / 2 + 0.6, W / 2 - colW / 2 - 0.6, i / (cols - 1));
    b.outlined([[cx - colW / 2, top], [cx + colW / 2, top], [cx + colW / 2 - 0.15, top - ch + 1], [cx - colW / 2 + 0.15, top - ch + 1]], 'wall', ARCH);
    b.outlined([[cx - colW / 2 - 0.4, top - ch + 1], [cx + colW / 2 + 0.4, top - ch + 1], [cx + colW / 2 + 0.4, top - ch], [cx - colW / 2 - 0.4, top - ch]], 'wall', ARCH * 0.8);
    b.lines([[[cx + 0.3, top - 0.3], [cx + 0.3, top - ch + 1.3]]], FINE * 0.6, 'ink', 0.6);
  }
  const ent = top - ch;
  b.outlined([[-W / 2 - 0.6, ent], [W / 2 + 0.6, ent], [W / 2 + 0.6, ent - 2.4], [-W / 2 - 0.6, ent - 2.4]], 'wall', ARCH);
  b.lines([[[-W / 2, ent - 1.2], [W / 2, ent - 1.2]]], FINE * 0.6, 'ink', 0.6);
  const et = ent - 2.4;
  if (r.chance(0.3)) {
    // domed variant
    const dr = W * 0.32;
    b.outlined([[-dr, et], [dr, et], [dr, et - 3], [-dr, et - 3]], 'wall', ARCH);
    const dome = arc(0, et - 3, dr, dr * 0.85, Math.PI, Math.PI * 2, 11);
    b.outlined(dome, 'roof', ARCH, true);
    b.fill(arc(0, et - 3, dr, dr * 0.85, Math.PI * 1.62, Math.PI * 2, 6).concat([[dr * 0.25, et - 3]]), 'ink', true, 0.22);
    b.lines([[[0, et - 3 - dr * 0.85], [0, et - 3 - dr * 0.85 - 2.5]]], 0.6, 'ink');
  } else {
    const ph = W * r.range(0.16, 0.22);
    const ped: Pt[] = [[-W / 2 - 1, et], [W / 2 + 1, et], [0, et - ph]];
    b.outlined([[W / 2 + 1, et], [W / 2 + 1 + dx, et - dy], [dx, et - ph - dy], [0, et - ph]], 'roof', ARCH);
    b.outlined(ped, 'wall', ARCH);
    b.lines([[[-W / 2 + 2.5, et - 0.8], [W / 2 - 2.5, et - 0.8], [0, et - ph + 1.6], [-W / 2 + 2.5, et - 0.8]]], FINE * 0.7, 'ink', 0.7);
  }
  b.ink([groundDash(r, -W / 2 - 8, -W / 2, 0.8, MID * 0.7), groundDash(r, W / 2 + 2, W / 2 + 10, 0.8, MID * 0.7)]);
  return b.finish([0, 0]);
}

function boat(b: InkBuilder, r: Rng, x: number, y: number, s: number, dir: 1 | -1): void {
  const L = 10 * s * r.range(0.9, 1.1), h = 2.4 * s;
  const hull: Pt[] = [[-L / 2, -h], [L / 2 + 1.2 * s, -h - 0.8 * s], [L * 0.35, 0], [-L * 0.4, 0]];
  const mapP = (p: Pt): Pt => [x + p[0] * dir, y + p[1]];
  b.outlined(hull.map(mapP), 'roof', ARCH);
  const mast: Pt = mapP([-L * 0.05, -h]);
  const mt: Pt = [mast[0], mast[1] - 9 * s];
  b.lines([[mast, mt]], 0.6, 'ink');
  const sail: Pt[] = [[mt[0] + dir * 0.4, mt[1] + 1], [mt[0] + dir * 5.5 * s, mast[1] - 1.2 * s], [mt[0] + dir * 0.4, mast[1] - 1.2 * s]];
  b.outlined(sail, 'paper', ARCH);
  b.fill([sail[0]!, sail[1]!, [mt[0] + dir * 2.6 * s, mast[1] - 1.2 * s]], 'shade', false);
}

function waterLines(r: Rng, x0: number, x1: number, y: number, n: number): Pt[][] {
  const out: Pt[][] = [];
  for (let i = 0; i < n; i++) {
    const a = lerp(x0, x1, r.range(0, 0.3)) + i * 1.5, c = lerp(x0, x1, r.range(0.7, 1)) - i * 1.5;
    const yy = y + i * 1.7;
    const pts: Pt[] = [];
    const k = Math.max(3, Math.round((c - a) / 3));
    for (let j = 0; j <= k; j++) pts.push([lerp(a, c, j / k), yy + (j % 2 === 0 ? 0 : -0.5)]);
    out.push(pts);
  }
  return out;
}

function genPort(r: Rng): Stamp {
  const b = new InkBuilder();
  const dir = r.sign();
  // shore
  const shore: Pt[] = [[-24, 2], [-10, 3.2], [2, 2.6], [6, 1.5]].map(([x, y]) => [x * dir, y] as Pt);
  b.fill(([[-24 * dir, -16]] as Pt[]).concat(shore, [[6 * dir, -16]]), 'paper', false);
  const n = r.int(2, 4);
  const pos = scatter(r, n, 9, 3, 7, -9 * dir, -2);
  pos.sort((a, c) => a[1] - c[1]);
  for (const q of pos) house(b, r, q[0] - 4, q[1], r.range(1.15, 1.35));
  // pier
  const px0 = 0, px1 = r.range(18, 26), py = r.range(1, 3);
  const deck: Pt[] = [[px0, py], [px1, py], [px1 + 1.2, py - 1.2], [px0 + 1.2, py - 1.2]].map(([x, y]) => [x * dir, y] as Pt);
  const posts: Pt[][] = [];
  for (let x = px0 + 2; x <= px1; x += 3.5) posts.push([[x * dir, py], [x * dir, py + 2.2]]);
  b.lines(posts, 0.9, 'ink');
  b.outlined(deck, 'wall', ARCH);
  const planks: Pt[][] = [];
  for (let x = px0 + 1.5; x < px1; x += 1.5) planks.push([[x * dir, py], [(x + 1.2) * dir, py - 1.2]]);
  b.lines(planks, FINE * 0.5, 'ink', 0.5);
  b.ink(taper(shore, penProfile(MID * 1.2, 0.2, 0.2, 0.05)));
  boat(b, r, (px1 * 0.6) * dir, py + 4.5, r.range(0.8, 1), dir);
  b.lines(waterLines(r, 4 * dir, (px1 + 6) * dir, py + 6.5, 2).concat(waterLines(r, -18 * dir, -4 * dir, 5.5, 1)), FINE, 'ink', 0.75);
  return b.finish([0, 0]);
}

function genLighthouse(r: Rng): Stamp {
  const b = new InkBuilder();
  drawBoulder(b, r, -5, 1, r.range(4, 5), MID);
  drawBoulder(b, r, 5, 1.6, r.range(3.5, 4.5), MID);
  const bw = r.range(4.2, 5), tw = bw * r.range(0.6, 0.72), H = r.range(24, 30);
  const y0 = -2.5;
  const body: Pt[] = [[-bw, y0], [bw, y0], [tw, y0 - H], [-tw, y0 - H]];
  b.outlined(body, 'wall', ARCH);
  const stripes = r.int(2, 3);
  for (let i = 0; i < stripes; i++) {
    const t0 = (i * 2 + 0.5) / (stripes * 2 + 0.5), t1 = t0 + 0.75 / (stripes * 2 + 0.5);
    const wa = lerp(bw, tw, t0), wb = lerp(bw, tw, t1);
    b.outlined([[-wa, y0 - H * t0], [wa, y0 - H * t0], [wb, y0 - H * t1], [-wb, y0 - H * t1]], 'roof', ARCH);
  }
  b.fill([[bw * 0.3, y0], [bw, y0], [tw, y0 - H], [tw * 0.3, y0 - H]], 'ink', false, 0.22);
  b.lines([body], ARCH, 'ink');
  const gy = y0 - H;
  b.outlined([[-tw - 1.4, gy], [tw + 1.4, gy], [tw + 1.4, gy - 1], [-tw - 1.4, gy - 1]], 'wall', ARCH);
  const lw = tw * 0.8, lh = 3.6;
  b.outlined([[-lw, gy - 1], [lw, gy - 1], [lw, gy - 1 - lh], [-lw, gy - 1 - lh]], 'light', ARCH);
  b.lines([[[0, gy - 1], [0, gy - 1 - lh]]], FINE * 0.7, 'ink');
  const cap: Pt[] = [[-lw - 0.8, gy - 1 - lh], [lw + 0.8, gy - 1 - lh], [0, gy - 1 - lh - 3.4]];
  b.outlined(cap, 'roof', ARCH);
  const rays: Pt[][] = [];
  const ly = gy - 1 - lh / 2;
  const nr = r.int(2, 3);
  for (let i = 0; i < nr; i++) {
    const a = (i - (nr - 1) / 2) * 0.32;
    for (const s of [-1, 1]) rays.push([[s * (lw + 1.6), ly + Math.sin(a) * 1.5], [s * (lw + r.range(6, 9)), ly + Math.sin(a) * 6]]);
  }
  b.lines(rays, FINE * 0.8, 'ink', 0.8);
  b.ink([[-1, y0 + 0.2], [-1, y0 - 2.5], [0, y0 - 3.2], [1, y0 - 2.5], [1, y0 + 0.2]], true);
  b.lines(waterLines(r, -12, 12, 3, 2), FINE, 'ink', 0.75);
  return b.finish([0, 0]);
}

// ================================================================ water

function genWave(r: Rng): Stamp {
  const b = new InkBuilder();
  const s = r.range(0.85, 1.15);
  const dir = r.sign();
  const lift = r.range(0.85, 1.15);
  // a swell rising to a crest that curls over into a small spiral
  const base: Pt[] = [[-9, 0.6], [-5, 0], [-1, -1.3], [2.5, -3.1], [5, -4.6], [7, -5.2], [8.7, -4.7], [9.4, -3.5], [8.9, -2.4], [7.8, -2.1], [7.1, -2.8], [7.5, -3.6], [8.2, -3.5]];
  const pts = base.map(([x, y]) => [x * s * dir, y * s * lift] as Pt);
  const ink: Pt[][] = [taper(pts, (t) => 1.35 * Math.pow(Math.sin(Math.PI * Math.min(1, 0.1 + t * 0.95)), 0.6) + 0.1)];
  // inner body line and foam line under the swell
  ink.push(taper([[0.5 * s * dir, -0.6 * s * lift], [4 * s * dir, -2.3 * s * lift], [6.8 * s * dir, -3.4 * s * lift]], penProfile(0.55, 0.4, 0.3, 0.05)));
  const n = r.int(1, 2);
  for (let i = 0; i < n; i++) {
    const y = 1.6 + i * 1.5;
    ink.push(taper([[(-7 + i * 2) * s * dir, y], [0, y - 0.5], [(6 - i * 1.5) * s * dir, y - 0.1]], penProfile(0.6, 0.4, 0.4, 0.05)));
  }
  b.ink(ink);
  return b.finish([0, 0]);
}

function genShip(r: Rng): Stamp {
  const b = new InkBuilder();
  const dir = r.sign();
  const L = r.range(26, 32), hh = r.range(4.2, 5.5);
  const masts = r.int(1, 3);
  const X = (x: number): number => x * dir;
  const stern = -L / 2, bow = L / 2;
  const deckY = (x: number): number => -hh - (x < stern + 6 ? 2.2 : 0) - Math.pow((x - stern) / L, 2) * 1.2;
  const hull: Pt[] = [[stern, -hh - 2.6], [stern + 6, -hh - 2.4], [stern + 6.3, -hh], [bow - 4, -hh - 0.9], [bow + 2, -hh - 2.2], [bow - 2.5, -0.8], [bow - 8, 0], [stern + 4, 0], [stern + 1, -1.5]];
  const hullP = hull.map(([x, y]) => [X(x), y] as Pt);
  // rigging + bowsprit behind sails
  const mastXs: number[] = [];
  for (let i = 0; i < masts; i++) mastXs.push(lerp(stern + L * 0.32, bow - L * 0.22, masts === 1 ? 0.45 : i / (masts - 1)));
  const mastH = (i: number): number => (masts === 1 ? 24 : i === Math.floor(masts / 2) ? 26 : 21) * r.range(0.92, 1.05);
  const rig: Pt[][] = [[[X(bow + 2), -hh - 2.2], [X(bow + 9), -hh - 5]]];
  for (let i = 0; i < masts; i++) {
    const mx = mastXs[i]!, top = deckY(mx) - mastH(i);
    rig.push([[X(mx), top], [X(bow + 8), -hh - 4.8]]);
    rig.push([[X(mx), top], [X(stern + 1), -hh - 2.6]]);
  }
  b.lines(rig, FINE * 0.6, 'ink', 0.75);
  b.lines(mastXs.map((mx, i) => [[X(mx), deckY(mx) + 0.5], [X(mx), deckY(mx) - mastH(i) - 1]] as Pt[]), 0.9, 'ink');
  // sails: stacked, billowing toward travel direction
  for (let i = 0; i < masts; i++) {
    const mx = mastXs[i]!, base = deckY(mx) - 2.5, top = base - mastH(i) + 4;
    const tiers = mastH(i) > 22 ? 3 : 2;
    const sw = r.range(5, 6.5) * (masts === 1 ? 1.3 : 1);
    for (let k = 0; k < tiers; k++) {
      const y1 = lerp(base, top, k / tiers) - 0.3, y0 = lerp(base, top, (k + 1) / tiers) + 0.3;
      const w1 = sw * (1 - k * 0.18), w0 = sw * (1 - (k + 1) * 0.18);
      const belly = r.range(1.2, 2);
      const sail: Pt[] = [[X(mx - w0), y0], [X(mx + w0), y0], ...quad([X(mx + w0), y0], [X(mx + w1 + belly), (y0 + y1) / 2], [X(mx + w1), y1], 4).slice(1),
        ...quad([X(mx - w1), y1], [X(mx), y1 + belly * 1.2], [X(mx - w1), y1], 3).slice(1), ...quad([X(mx - w1), y1], [X(mx - w1 + belly * 0.6), (y0 + y1) / 2], [X(mx - w0), y0], 4).slice(1, -1)];
      b.outlined(sail, 'paper', ARCH, true);
      b.fill([[X(mx + w0 * 0.3), y0 + 0.3], [X(mx + w0), y0], [X(mx + w1 + belly * 0.7), (y0 + y1) / 2], [X(mx + w1), y1], [X(mx + w1 * 0.3), y1 + belly * 0.6]], 'shade', true, 0.9);
      b.lines([[[X(mx - w0), y0], [X(mx + w0), y0]]], 0.8, 'ink');
    }
    flagPennant(b, r, X(mx), deckY(mx) - mastH(i) - 1, dir);
  }
  b.outlined(hullP, 'roof', ARCH * 1.2);
  b.fill([[X(stern + 4), 0], [X(bow - 8), 0], [X(bow - 2.5), -0.8], [X(bow - 4), -hh * 0.45], [X(stern + 2), -hh * 0.45]], 'ink', false, 0.25);
  b.lines([[[X(stern + 6.3), -hh + 1.2], [X(bow - 3.5), -hh + 0.4]]], FINE * 0.7, 'ink', 0.7);
  const ports: Pt[][] = [];
  for (let x = stern + 8; x < bow - 6; x += 3.2) ports.push(ellipse(X(x), -hh * 0.62, 0.45, 0.45, 5));
  b.ink(ports, false);
  const ink: Pt[][] = [];
  for (let i = 0; i < 3; i++) {
    const y = 1 + i * 1.6, x0 = stern - 2 - i * 3 + r.jit(1), x1 = bow + 1 - i * 4 + r.jit(1);
    ink.push(taper(waterLines(r, X(x0), X(x1), y, 1)[0]!, penProfile(0.8, 0.3, 0.3, 0.05)));
  }
  b.ink(ink);
  return b.finish([0, 0]);
}

function flagPennant(b: InkBuilder, r: Rng, x: number, y: number, dir: 1 | -1): void {
  const l = r.range(3.5, 5.5) * -dir;
  b.outlined([[x, y], [x + l * 0.5, y + 0.2 + r.jit(0.3)], [x + l, y + 0.8], [x + l * 0.5, y + 1.3], [x, y + 1.4]], 'roof', 0.4, true);
}

function genSeaMonster(r: Rng): Stamp {
  const b = new InkBuilder();
  const dir = r.sign();
  const coils = r.int(2, 3);
  const X = (x: number): number => x * dir;
  const ink: Pt[][] = [];
  let x = 0;
  const thick0 = r.range(4, 5);
  const humps: Array<{ x0: number; x1: number; h: number; th: number }> = [];
  for (let i = 0; i < coils; i++) {
    const w = r.range(9, 13) * (1 - i * 0.12), h = r.range(7, 10) * (1 - i * 0.15);
    humps.push({ x0: x, x1: x + w, h, th: thick0 * (1 - (i + 1) * 0.2) });
    x += w + r.range(3, 6);
  }
  const water: Pt[][] = [];
  // humps (back to front doesn't matter: they don't overlap)
  for (const hm of humps) {
    const c = arc((hm.x0 + hm.x1) / 2, 0, (hm.x1 - hm.x0) / 2, hm.h, Math.PI, Math.PI * 2, 9).map(([px, py]) => [X(px), py] as Pt);
    const band = taper(c, () => hm.th);
    b.fill(band, 'foliage');
    const under = taper(c.map(([px, py]) => [px, py + hm.th * 0.25] as Pt), (t) => hm.th * 0.45 * Math.pow(Math.sin(Math.PI * t), 0.3));
    b.fill(under, 'foliageDark', true, 0.9);
    const stripes: Pt[][] = [];
    for (let k = 1; k < 7; k++) {
      const p = pointAt(c, k / 7);
      const q = pointAt(c, k / 7 + 0.01);
      const nx = -(q[1] - p[1]), ny = q[0] - p[0];
      const l = Math.hypot(nx, ny) || 1;
      stripes.push([[p[0] + (nx / l) * hm.th * 0.1, p[1] + (ny / l) * hm.th * 0.1], [p[0] + (nx / l) * hm.th * 0.45, p[1] + (ny / l) * hm.th * 0.45]]);
    }
    b.lines(stripes, FINE * 0.7, 'ink', 0.8);
    const outer = c.map((p, i) => { const q = c[Math.min(i + 1, c.length - 1)]!, pp = c[Math.max(i - 1, 0)]!; const nx = q[1] - pp[1], ny = -(q[0] - pp[0]); const l = Math.hypot(nx, ny) || 1; const s = dir; return [p[0] - s * (nx / l) * hm.th / 2, p[1] - s * (ny / l) * hm.th / 2] as Pt; });
    const innerE = c.map((p, i) => { const q = c[Math.min(i + 1, c.length - 1)]!, pp = c[Math.max(i - 1, 0)]!; const nx = q[1] - pp[1], ny = -(q[0] - pp[0]); const l = Math.hypot(nx, ny) || 1; const s = dir; return [p[0] + s * (nx / l) * hm.th / 2, p[1] + s * (ny / l) * hm.th / 2] as Pt; });
    const topE = outer[0]![1] < innerE[0]![1] || Math.min(...outer.map((p) => p[1])) < Math.min(...innerE.map((p) => p[1])) ? outer : innerE;
    const botE = topE === outer ? innerE : outer;
    ink.push(taper(topE, penProfile(MID * 1.3, 0.1, 0.1, 0.2)));
    ink.push(taper(botE, penProfile(MID * 0.8, 0.1, 0.1, 0.2)));
    // dorsal spines
    for (let k = 1; k < 5; k++) {
      const p = pointAt(topE, k / 5 - 0.05), q = pointAt(topE, k / 5 + 0.05);
      const m = lerpPt(p, q, 0.3);
      ink.push([p, [m[0], m[1] - 2.2], q]);
    }
    for (const ex of [hm.x0, hm.x1]) water.push(...waterLines(r, X(ex - 4), X(ex + 4), 0.6, 2));
  }
  // neck + head at x<0 side
  const neck: Pt[] = [[X(-3), 0.3], [X(-6), -7], [X(-4), -14], [X(-8.5), -17]];
  const neckC = quad(neck[0]!, [X(-8.5), -6], neck[2]!, 6).concat([neck[3]!]);
  const nb = taper(neckC, (t) => lerp(thick0 * 1.1, thick0 * 0.75, t));
  b.fill(nb, 'foliage');
  ink.push(taper(neckC.map(([px, py]) => [px - dir * thick0 * 0.5, py] as Pt), penProfile(MID * 1.2, 0.1, 0.05, 0.3)));
  ink.push(taper(neckC.slice(0, 6).map(([px, py]) => [px + dir * thick0 * 0.5, py] as Pt), penProfile(MID * 0.9, 0.1, 0.2, 0.3)));
  const hx = -8.5, hy = -17;
  const head: Pt[] = [[hx + 3, hy - 1], [hx + 1, hy - 3.6], [hx - 3, hy - 3.8], [hx - 8, hy - 1.8], [hx - 8.6, hy - 0.8], [hx - 4, hy - 0.4], [hx - 8, hy + 1.2], [hx - 4.5, hy + 2.2], [hx + 2.5, hy + 2.5]].map(([px, py]) => [X(px), py] as Pt);
  b.outlined(head, 'foliage', MID * 0.9, false);
  b.fill([head[5]!, head[4]!, head[6]!], 'ink', false, 0.85);
  ink.push(ellipse(X(hx - 2), hy - 1.8, 0.7, 0.6, 6));
  // frill / horns
  ink.push(taper([[X(hx + 1.5), hy - 3], [X(hx + 4), hy - 6.5], [X(hx + 6.5), hy - 7.5]], penProfile(1.1, 0, 0.8, 0.1)));
  ink.push(taper([[X(hx + 2.6), hy - 1.2], [X(hx + 5.5), hy - 3.5], [X(hx + 7.5), hy - 3.8]], penProfile(0.9, 0, 0.8, 0.1)));
  // tail fluke at the far end
  const tx = x - r.range(1, 3);
  const fluke: Pt[] = [[tx, 0.3], [tx + 1.5, -3.5], [tx + 0.5, -7], [tx + 3.5, -5], [tx + 5.5, -7.5], [tx + 4.5, -3.2], [tx + 2.2, 0.3]].map(([px, py]) => [X(px), py] as Pt);
  b.outlined(fluke, 'foliage', MID * 0.9, true);
  water.push(...waterLines(r, X(-9), X(1), 0.6, 2), ...waterLines(r, X(tx - 3), X(tx + 7), 0.6, 2));
  b.ink(ink, true);
  b.lines(water, FINE, 'ink', 0.85);
  return b.finish([X(x / 2 - 4), 0]);
}

function genWhirlpool(r: Rng): Stamp {
  const b = new InkBuilder();
  const R = r.range(17, 22), sq = r.range(0.5, 0.6);
  b.fill(ellipse(0, 0, R * 1.05, R * sq * 1.05, 18), 'water', true, 0.55);
  const arms = r.int(2, 3);
  const turns = r.range(1.1, 1.5);
  const ink: Pt[][] = [];
  const spin = r.sign();
  for (let k = 0; k < arms; k++) {
    const a0 = (Math.PI * 2 * k) / arms + r.jit(0.3);
    const pts: Pt[] = [];
    const n = 22;
    for (let i = 0; i < n; i++) {
      const t = i / (n - 1);
      const a = a0 + spin * t * turns * Math.PI * 2;
      const rad = lerp(R, 2, Math.pow(t, 0.8));
      pts.push([Math.cos(a) * rad, Math.sin(a) * rad * sq]);
    }
    // break each arm into 2-3 pen strokes
    const cuts = [0, r.range(0.3, 0.45), r.range(0.6, 0.75), 1];
    for (let c = 0; c < cuts.length - 1; c++) {
      if (c === 2 && r.chance(0.3)) continue;
      const i0 = Math.round(cuts[c]! * (n - 1)) + (c > 0 ? 1 : 0), i1 = Math.round(cuts[c + 1]! * (n - 1));
      const seg = pts.slice(i0, i1 + 1);
      if (seg.length < 3) continue;
      const w = lerp(1.25, 0.45, cuts[c]!);
      ink.push(taper(seg, penProfile(w, 0.25, 0.4, 0.05)));
    }
  }
  ink.push(ellipse(0, 0, 1.6, 1.6 * sq, 8));
  b.ink(ink);
  return b.finish([0, 0]);
}

// ================================================================ decoration

function genCompass(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const R = 140;
  const rings = r.int(1, 2);
  const points = p['points']! >= 8 ? (p['points']! >= 24 ? 32 : p['points']! >= 12 ? 16 : 8) : r.pick([8, 16, 16, 32]);
  const ink: Pt[][] = [];
  // rings
  const ringR = R * r.range(0.66, 0.74);
  b.fill(ellipse(0, 0, ringR + 6, ringR + 6, 48), 'paper', true, 0.92);
  b.lines([ellipse(0, 0, ringR + 6, ringR + 6, 48), ellipse(0, 0, ringR, ringR, 48)], 1.2, 'ink', undefined, true);
  if (rings === 2) b.lines([ellipse(0, 0, ringR * 0.55, ringR * 0.55, 32)], 0.8, 'ink', undefined, true);
  const ticks: Pt[][] = [];
  const nt = r.pick([32, 64]);
  for (let i = 0; i < nt; i++) {
    const a = (Math.PI * 2 * i) / nt;
    const l = i % (nt / 8) === 0 ? 6 : i % 2 === 0 ? 4 : 2.4;
    ticks.push([[Math.cos(a) * ringR, Math.sin(a) * ringR], [Math.cos(a) * (ringR + l), Math.sin(a) * (ringR + l)]]);
  }
  b.lines(ticks, 0.7, 'ink');
  // star points: longest first so shorter ones sit on top? draw short (back) first
  const spec: Array<{ a: number; len: number; wid: number; tone: PaletteToken }> = [];
  for (let i = 0; i < points; i++) {
    const a = -Math.PI / 2 + (Math.PI * 2 * i) / points;
    const level = i % (points / 4) === 0 ? 0 : i % (points / 8) === 0 ? 1 : i % (points / 16) === 0 ? 2 : 3;
    const len = [R, R * 0.66, R * 0.46, R * 0.34][level]!;
    const wid = [R * 0.13, R * 0.1, R * 0.07, R * 0.05][level]!;
    spec.push({ a, len, wid, tone: level === 0 ? 'ink' : level === 1 ? 'shade' : 'ink' });
  }
  spec.sort((x, y) => x.len - y.len);
  for (const s of spec) {
    const tip: Pt = [Math.cos(s.a) * s.len, Math.sin(s.a) * s.len];
    const l: Pt = [Math.cos(s.a - Math.PI / 2) * s.wid, Math.sin(s.a - Math.PI / 2) * s.wid];
    const rr: Pt = [-l[0], -l[1]];
    b.outlined([[0, 0], l, tip], 'paper', 0.9);
    b.add([{ pts: [[0, 0], rr, tip], closed: true, smooth: false }], s.tone, 'ink', 0.9, s.tone === 'ink' ? 0.92 : undefined);
  }
  b.outlined(ellipse(0, 0, R * 0.05, R * 0.05, 12), 'paper', 0.9, true);
  ink.push(ellipse(0, 0, R * 0.018, R * 0.018, 8));
  // north mark: fleur-de-lis or an inked N
  const ny = -R - 16;
  if (r.chance(0.5)) {
    const s = 13;
    ink.push(taper([[-s * 0.75, ny + s * 0.9], [-s * 0.75, ny - s * 0.9], [s * 0.75, ny + s * 0.9], [s * 0.75, ny - s * 0.9]], (t) => (t > 0.33 && t < 0.67 ? 4 : 1.8)));
    ink.push(taper([[-s * 1.2, ny + s * 0.9], [-s * 0.3, ny + s * 0.9]], 1.6), taper([[s * 0.3, ny - s * 0.9], [s * 1.2, ny - s * 0.9]], 1.6));
  } else {
    const s = 15;
    const petal: Pt[] = [[0, ny + s], [-s * 0.35, ny - s * 0.1], [0, ny - s * 1.3], [s * 0.35, ny - s * 0.1]];
    b.outlined(petal, 'ink', 0.9, true);
    for (const sd of [-1, 1]) {
      const side: Pt[] = [[sd * s * 0.2, ny + s * 0.4], [sd * s * 0.9, ny - s * 0.6], [sd * s * 1.2, ny + s * 0.1], [sd * s * 0.7, ny + s * 0.3]];
      b.outlined(side, 'paper', 0.9, true);
    }
    ink.push(taper([[-s * 0.8, ny + s * 0.55], [s * 0.8, ny + s * 0.55]], penProfile(2, 0.3, 0.3, 0.3)));
  }
  b.ink(ink);
  return b.finish([0, 0]);
}

// ================================================================ registry

type GenFn = (r: Rng, p: Params) => Stamp;

function def(id: string, label: string, category: StampCategory, defaultSize: number, fn: GenFn, params?: ParamSpec, fixed?: Params): GeneratorInfo {
  const info: GeneratorInfo = {
    id, label, category, defaultSize,
    generate(seed: number, given?: Params): Stamp {
      const p = resolveParams(params, given);
      if (fixed) for (const k of Object.keys(fixed)) if (given?.[k] === undefined) p[k] = fixed[k]!;
      return fn(new Rng(mixSeed(id, seed >>> 0)), p);
    },
  };
  if (params) info.params = params;
  return info;
}

const MOUNTAIN_PARAMS: ParamSpec = {
  peaks: { min: 0, max: 3, default: 0 }, // 0 = pick from the seed
  snow: { min: 0, max: 1, default: 0 },
  height: { min: 0.6, max: 1.4, default: 1 },
  steep: { min: 0.7, max: 1.5, default: 1 },
};

const LIST: GeneratorInfo[] = [
  def('mountain', 'Mountain', 'mountain', 18, genMountain, MOUNTAIN_PARAMS),
  def('mountainSnow', 'Snowy mountain', 'mountain', 18, genMountain, MOUNTAIN_PARAMS, { snow: 1 }),
  def('volcano', 'Volcano', 'mountain', 18, genVolcano, { smoke: { min: 0, max: 1, default: 0.6 } }),
  def('hill', 'Hill', 'hill', 5, genHill, { width: { min: 0.7, max: 1.4, default: 1 } }),
  def('conifer', 'Conifer', 'tree', 6, genConifer),
  def('deciduous', 'Deciduous tree', 'tree', 6, genDeciduous),
  def('palm', 'Palm', 'tree', 6, genPalm),
  def('deadTree', 'Dead tree', 'tree', 5, genDeadTree),
  def('shrub', 'Shrub', 'vegetation', 2.5, genShrub),
  def('grassTuft', 'Grass tuft', 'vegetation', 2, genGrassTuft),
  def('reeds', 'Reeds (swamp)', 'vegetation', 4, genReeds),
  def('cactus', 'Cactus', 'vegetation', 5, genCactus),
  def('dunes', 'Dunes', 'terrain', 4, genDunes),
  def('rock', 'Rocks', 'terrain', 3, genRock),
  def('glacierCrack', 'Glacier crevasses', 'terrain', 4, genCrevasse),
  def('village', 'Village', 'settlement', 8, genVillage, { houses: { min: 0, max: 5, default: 0 } }),
  def('town', 'Town', 'settlement', 11, genTown),
  def('city', 'City', 'settlement', 16, (r) => walledCity(r, false)),
  def('capital', 'Capital', 'settlement', 22, (r) => walledCity(r, true)),
  def('castle', 'Castle', 'structure', 11, genCastle),
  def('tower', 'Tower', 'structure', 8, genTower),
  def('ruins', 'Ruins', 'structure', 5, genRuins),
  def('temple', 'Temple', 'structure', 8, genTemple),
  def('port', 'Port', 'settlement', 8, genPort),
  def('lighthouse', 'Lighthouse', 'structure', 9, genLighthouse),
  def('wave', 'Wave', 'water', 2, genWave),
  def('ship', 'Ship', 'water', 8, genShip),
  def('seaMonster', 'Sea monster', 'water', 6, genSeaMonster),
  def('whirlpool', 'Whirlpool', 'water', 5, genWhirlpool),
  def('compass', 'Compass rose', 'decoration', 60, genCompass, { points: { min: 0, max: 32, default: 0 } }),
];

export const GENERATORS: Record<string, GeneratorInfo> = Object.fromEntries(LIST.map((g) => [g.id, g]));

export function makeStamp(id: string, seed: number, params?: Record<string, number>): Stamp {
  const g = GENERATORS[id];
  if (!g) throw new Error(`Unknown stamp generator: ${id}`);
  return g.generate(seed, params);
}

/** Map units per box unit when the stamp is drawn at `size` map units (default: its defaultSize). */
export function stampScale(id: string, size?: number): number {
  const g = GENERATORS[id];
  if (!g) throw new Error(`Unknown stamp generator: ${id}`);
  return (size ?? g.defaultSize) / (g.defaultSize * UNITS_PER_MAP_UNIT);
}

/** Inner SVG markup (paths in box coordinates), no <svg> wrapper. */
export function stampToSvg(stamp: Stamp, palette: Record<PaletteToken, string> = DEFAULT_PALETTE): string {
  let out = '<g stroke-linecap="round" stroke-linejoin="round">';
  for (const s of stamp.shapes) {
    let a = `<path d="${s.d}" fill="${palette[s.fill] ?? 'none'}"`;
    if (s.stroke !== 'none') a += ` stroke="${palette[s.stroke]}" stroke-width="${s.width ?? 1}"`;
    if (s.opacity !== undefined) a += ` opacity="${s.opacity}"`;
    out += a + '/>';
  }
  return out + '</g>';
}
