// Mountain, volcano and hill stamps.
import { InkBuilder, Rng, lerp, lerpPt, clamp, pointAt, wobble, smoothNoise, taper, penProfile, arc, ellipse, crossY, loopStroke, groundDash, polyLength } from '../ink';
import type { Pt, Stamp, WidthFn } from '../ink';
import { OUT, MID, FINE, smooth01, Params } from './common';
import { lumpy, shadeCrescent, gapStart } from './vegetation';

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

export function genMountain(r: Rng, p: Params): Stamp {
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

export function genVolcano(r: Rng, p: Params): Stamp {
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

export function genHill(r: Rng, p: Params): Stamp {
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
