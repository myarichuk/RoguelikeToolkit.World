// Settlements and structures: villages to capitals, castles, ruins, temples, ports.
import { InkBuilder, Rng, lerp, pointAt, wobble, taper, penProfile, arc, ellipse, loopStroke, groundDash, jitter, polyLength, translate } from '../ink';
import type { Pt, Stamp } from '../ink';
import { OUT, MID, FINE, ARCH, Params } from './common';
import { lumpy, shadeCrescent, gapStart, drawBoulder, blade } from './vegetation';

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

export function genVillage(r: Rng, p: Params): Stamp {
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

export function genTown(r: Rng): Stamp {
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

export function walledCity(r: Rng, capital: boolean): Stamp {
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

export function genCastle(r: Rng): Stamp {
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

export function genTower(r: Rng): Stamp {
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

export function genRuins(r: Rng): Stamp {
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

export function genTemple(r: Rng): Stamp {
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

export function waterLines(r: Rng, x0: number, x1: number, y: number, n: number): Pt[][] {
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

export function genPort(r: Rng): Stamp {
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

export function genLighthouse(r: Rng): Stamp {
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
