// Sea stamps: waves, ships, monsters, whirlpools.
import { InkBuilder, Rng, lerp, lerpPt, pointAt, taper, penProfile, arc, ellipse, quad } from '../ink';
import type { Pt, Stamp } from '../ink';
import { MID, FINE, ARCH } from './common';
import { waterLines } from './architecture';

// ================================================================ water

export function genWave(r: Rng): Stamp {
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

export function genShip(r: Rng): Stamp {
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

export function genSeaMonster(r: Rng): Stamp {
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

export function genWhirlpool(r: Rng): Stamp {
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
