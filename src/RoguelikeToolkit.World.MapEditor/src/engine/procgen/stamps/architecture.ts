// Settlements and structures: villages to capitals, castles, ruins, temples, ports.
import { InkBuilder, Rng, lerp, lerpPt, pointAt, wobble, taper, penProfile, arc, ellipse, loopStroke, groundDash, jitter, polyLength, translate, quad } from '../ink';
import type { PaletteToken, Pt, Stamp } from '../ink';
import { OUT, MID, FINE, ARCH } from './common';
import type { Params, ParamSpec } from './common';
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

type Roof = 'cone' | 'crenel' | 'dome' | 'needle' | 'spikes';
interface TowerOpts { roof: Roof; flag?: boolean; windows?: number; }

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
  } else if (o.roof === 'needle') {
    topY = needleRoof(b, r, cx, y - h, hw * 1.08, e * 1.2, w * r.range(2.6, 3.4), w);
  } else if (o.roof === 'spikes') {
    topY = spikeCrown(b, r, cx - hw * 1.12, cx + hw * 1.12, y - h, w * 0.28, w * 0.7);
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
  const st = archStyle(p);
  backdrop(b, r, st, 17, 6, 12);
  const n = p['houses']! >= 1 ? Math.round(p['houses']!) : r.int(2, 4);
  const pos = scatter(r, n, 10, 4, 7);
  if (st.wall !== WALL_NONE) ring(b, r, st, 0, 0.5, 15, 7, Math.PI, Math.PI * 2, 2.6, false);
  pos.sort((a, c) => a[1] - c[1]);
  const extras: Array<{ y: number; draw: () => void }> = [];
  if (r.chance(0.55)) {
    const tx = r.sign() * r.range(10, 15), ty = r.range(-3, 2);
    extras.push({ y: ty, draw: () => smallTree(b, r, tx, ty, 0.7) });
  }
  const items = pos.map((q) => ({ y: q[1], draw: () => dwelling(b, r, st, q[0] - 4, q[1], r.range(1.25, 1.5)) })).concat(extras);
  items.sort((a, c) => a.y - c.y);
  for (const it of items) it.draw();
  if (st.wall !== WALL_NONE) ring(b, r, st, 0, 0.5, 15, 7, 0, Math.PI, 2.6, true);
  const ink: Pt[][] = [];
  ink.push(groundDash(r, -14, -4, 4.2, MID * 0.6));
  ink.push(groundDash(r, 2, 15, 4.6, MID * 0.6));
  b.ink(ink);
  if (r.chance(0.6)) {
    const path = wobble([[r.jit(3), 5], [r.range(-6, 6), 10], [r.range(-10, 10), 15]], r, 0.6, 3);
    b.lines([path, translate(path, 2.2, 0)], FINE * 0.8, 'ink', 0.65, true);
  }
  return done(b, st);
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

export function genTown(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const st = archStyle(p);
  const n = r.int(5, 8);
  const pos = scatter(r, n, 17, 7, 7.5);
  const cpos: Pt = [r.jit(4), -r.range(3, 6)];
  const items: Array<{ y: number; draw: () => void }> = pos
    .filter((q) => Math.hypot(q[0] - cpos[0], (q[1] - cpos[1]) * 2) > 9)
    .map((q) => ({ y: q[1], draw: () => dwelling(b, r, st, q[0] - 4, q[1], r.range(1.15, 1.4)) }));
  items.push({ y: cpos[1], draw: () => centrepiece(b, r, st, cpos[0] - 4, cpos[1], 1.1) });
  items.sort((a, c) => a.y - c.y);
  if (st.crag) cliff(b, r, -30, 30, -4, r.range(16, 22));
  b.fill(ellipse(0, 1, 21, 9, 14), 'paper');
  if (st.water) shallows(b, r, 0, 2, 22, 9);
  for (const it of items) it.draw();
  // partial wall on the front, with a tower
  const a0 = r.range(0.05, 0.5) * Math.PI, a1 = a0 + r.range(0.35, 0.55) * Math.PI;
  ring(b, r, st, 0, 0, 24, 11, a0, a1, 3.4, true);
  const tp = arc(0, 0, 24, 11, a1, a1, 1)[0]!;
  styledTower(b, r, st, tp[0], tp[1] + 0.5, 4.5, 7, { roof: r.chance(0.5) ? 'cone' : 'crenel', windows: 1 });
  if (r.chance(0.5)) {
    const tq = arc(0, 0, 24, 11, a0, a0, 1)[0]!;
    styledTower(b, r, st, tq[0], tq[1] + 0.5, 4.2, 6.4, { roof: 'crenel', windows: 1 });
  }
  return done(b, st);
}

function keep(b: InkBuilder, r: Rng, st: ArchStyle, x: number, y: number, w: number, h: number, capital: boolean): void {
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
  styledTower(b, r, st, x + dx, y - h - dy + 0.5, tw * 0.9, h * 0.25, { roof, windows: 0 });
  styledTower(b, r, st, x + w + dx, y - h - dy + 0.5, tw * 0.9, h * 0.25, { roof, windows: 0, flag: capital });
  styledTower(b, r, st, x, y - h + 0.5, tw, h * 0.3, { roof, windows: 1, flag: !capital && r.chance(0.6) });
  styledTower(b, r, st, x + w, y - h + 0.5, tw, h * 0.3, { roof, windows: 1 });
  if (capital) styledTower(b, r, st, x + w * 0.5 + dx * 0.5, y - h - dy * 0.5, tw * 1.3, h * 0.55, { roof: r.chance(0.5) ? 'dome' : 'cone', windows: 1, flag: true });
}

export function walledCity(r: Rng, capital: boolean, p: Params = {}): Stamp {
  const b = new InkBuilder();
  const st = archStyle(p);
  const R = capital ? r.range(46, 54) : r.range(30, 38);
  const ry = R * r.range(0.4, 0.46);
  const wh = capital ? 6.5 : 5;
  if (st.crag) cliff(b, r, -R - 10, R + 10, -ry * 0.6, R * r.range(0.45, 0.6));
  b.fill(ellipse(0, 0, R + 1, ry + 1, 20).map(([x, y]) => [x, y < 0 ? y - wh : y] as Pt), 'paper');
  if (st.water) shallows(b, r, 0, 0, R * 0.95, ry * 0.95);
  // back wall and back towers
  ring(b, r, st, 0, 0, R, ry, Math.PI, Math.PI * 2, wh, false);
  for (const a of [1.25, 1.5, 1.75]) {
    if (a === 1.5 && !capital) continue;
    const tp = arc(0, 0, R, ry, a * Math.PI, a * Math.PI, 1)[0]!;
    styledTower(b, r, st, tp[0], tp[1] + 0.5, wh * 1.0, wh * 1.5, { roof: 'crenel', windows: 0 });
  }
  if (capital) {
    // inner citadel wall
    ring(b, r, st, 0, -ry * 0.3, R * 0.42, ry * 0.42, Math.PI, Math.PI * 2, wh * 0.8, false);
  }
  const nh = capital ? r.int(20, 26) : r.int(11, 15);
  const pos = scatter(r, nh, R * 0.86, ry * 0.8, capital ? 6 : 6.2, 0, -1);
  const kp: Pt = capital ? [r.jit(4) - 6, -ry * 0.35] : [r.jit(R * 0.25) - 4, -ry * r.range(0.1, 0.35)];
  const kw = capital ? 14 : 9;
  const items: Array<{ y: number; draw: () => void }> = pos
    .filter((q) => !(q[0] > kp[0] - 6 && q[0] < kp[0] + kw + 6 && Math.abs(q[1] - kp[1]) < (capital ? 8 : 5)))
    .map((q) => ({ y: q[1], draw: () => dwelling(b, r, st, q[0] - 3.5, q[1], r.range(0.95, 1.15)) }));
  items.push({ y: kp[1], draw: () => stronghold(b, r, st, kp[0], kp[1], kw, capital ? 16 : 11, capital) });
  if (r.chance(capital ? 1 : 0.6)) {
    const cp = scatter(r, 1, R * 0.5, ry * 0.4, 0, R * 0.25 * r.sign(), 0)[0] ?? [R * 0.3, 0];
    items.push({ y: cp[1] + 0.01, draw: () => centrepiece(b, r, st, cp[0], cp[1], 0.9) });
  }
  if (capital) {
    // inner wall front side drawn in the keep's row
    items.push({ y: kp[1] + 0.5, draw: () => ring(b, r, st, 0, -ry * 0.3, R * 0.42, ry * 0.42, 0, Math.PI, wh * 0.8, true) });
  }
  items.sort((a, c) => a.y - c.y);
  for (const it of items) it.draw();
  // front wall
  ring(b, r, st, 0, 0, R, ry, 0, Math.PI, wh, true);
  // gate
  const gx = r.jit(R * 0.15), gy = ry * Math.sqrt(1 - (gx / R) ** 2);
  if (st.wall !== WALL_NONE) b.ink([[gx - 1.6, gy + 0.3], [gx - 1.6, gy - wh * 0.6], [gx, gy - wh * 0.8], [gx + 1.6, gy - wh * 0.6], [gx + 1.6, gy + 0.3]], true);
  const tset = capital ? [0, 0.2, 0.4, 0.6, 0.8, 1] : [0, 0.28, 0.72, 1];
  for (const t of tset) {
    const a = t * Math.PI;
    const tp = arc(0, 0, R, ry, a, a, 1)[0]!;
    styledTower(b, r, st, tp[0], tp[1] + 0.5, wh * 1.25, wh * 1.9, { roof: capital && (t === 0 || t === 1) ? 'cone' : 'crenel', windows: 1, flag: capital && t === 0.4 });
  }
  if (st.wall !== WALL_NONE) for (const s of [-1, 1]) styledTower(b, r, st, gx + s * 2.8, gy + 0.5, wh * 0.9, wh * 1.6, { roof: 'crenel', windows: 0 });
  const ink: Pt[][] = [];
  ink.push(groundDash(r, -R - 6, -R * 0.6, ry * 0.75 + 1.5, MID * 0.7));
  ink.push(groundDash(r, R * 0.55, R + 8, ry * 0.8 + 1.5, MID * 0.8));
  b.ink(ink);
  return done(b, st);
}

export function genCastle(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const st = archStyle(p);
  const R = r.range(18, 24), ry = R * 0.42, wh = 5;
  if (st.crag) cliff(b, r, -R - 14, R + 14, -ry * 0.4, R * r.range(0.8, 1));
  // mound
  const mound: Pt[] = [];
  for (let i = 0; i <= 10; i++) { const t = i / 10; mound.push([lerp(-R - 9, R + 9, t), ry + 3 - Math.pow(Math.sin(Math.PI * t), 0.6) * (ry + 6)]); }
  b.fill(mound, 'paper');
  b.fill(mound.slice(6).concat(mound.slice(6).map(([x, y]) => [x - 3, lerp(y, ry + 3, 0.6)] as Pt).reverse()), 'shade', true, 0.8);
  b.ink(taper(mound, penProfile(OUT * 0.8, 0.25, 0.2, 0.05)));
  ring(b, r, st, 0, 0, R, ry, Math.PI, Math.PI * 2, wh, false);
  const kx = r.jit(4) - 6;
  stronghold(b, r, st, kx, -ry * 0.15, r.range(10, 13), r.range(15, 20), false);
  ring(b, r, st, 0, 0, R, ry, 0, Math.PI, wh, true);
  const nt = r.int(2, 4);
  const ts = nt === 2 ? [0, 1] : nt === 3 ? [0, 0.5, 1] : [0, 0.33, 0.67, 1];
  for (const t of ts) {
    const tp = arc(0, 0, R, ry, t * Math.PI, t * Math.PI, 1)[0]!;
    styledTower(b, r, st, tp[0], tp[1] + 0.5, 6, 10, { roof: r.chance(0.4) ? 'cone' : 'crenel', windows: 1, flag: r.chance(0.25) });
  }
  if (nt !== 3 && st.wall !== WALL_NONE) b.ink([[-1.6, ry + 0.3], [-1.6, ry - 3], [0, ry - 4], [1.6, ry - 3], [1.6, ry + 0.3]], true);
  b.ink(groundDash(r, R * 0.4, R + 12, ry + 3.4, MID * 0.7));
  return done(b, st);
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

export function genRuins(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const st = archStyle(p);
  if (st.crag) cliff(b, r, -26, 26, -1, r.range(12, 17));
  if (st.water) shallows(b, r, 0, 2.5, 22, 6);
  if (st.ruin !== 0) { styledRuins(b, r, st); return done(b, st); }
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
  return done(b, st);
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

export function genPort(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const st = archStyle(p);
  const dir = r.sign();
  // shore
  const shore: Pt[] = [[-24, 2], [-10, 3.2], [2, 2.6], [6, 1.5]].map(([x, y]) => [x * dir, y] as Pt);
  b.fill(([[-24 * dir, -16]] as Pt[]).concat(shore, [[6 * dir, -16]]), 'paper', false);
  if (st.crag) cliff(b, r, Math.min(-26 * dir, 2 * dir), Math.max(-26 * dir, 2 * dir), -5, r.range(13, 18));
  const n = r.int(2, 4);
  const pos = scatter(r, n, 9, 3, 7, -9 * dir, -2);
  pos.sort((a, c) => a[1] - c[1]);
  for (const q of pos) dwelling(b, r, st, q[0] - 4, q[1], r.range(1.15, 1.35));
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
  return done(b, st);
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

// ================================================================ architectural styles
//
// One generic vocabulary behind every settlement, fort and ruin generator. A culture's look is
// nothing but numbers in its content-pack asset's `params`; the generators know no cultures.
// Every default reproduces the classic look exactly (same drawing, same random stream).
//
//  house      dwelling: 0 gabled cottage, 1 flat-roofed block, 2 domed round hut with a chimney,
//             3 hide tent or yurt, 4 grassy burrow mound with a round door, 5 thatched hut on stilts,
//             6 turf longhouse, 7 tree dwelling (big tree with a platform hut), 8 steep pointed hall
//  alt, mix   a second dwelling kind and the share of dwellings drawn in it (mix 0 = never)
//  wall       enclosure: 0 crenellated stone, 1 sharpened-stake palisade, 2 hedge / thorn ring,
//             3 mudbrick, 4 reed fence, 5 none, 6 spiked stone, 7 smooth parapet
//  wallH      wall height multiplier
//  towerForm  0 round stone, 1 square stone, 2 timber watch platform, 3 tree with a platform
//  spire      tower roof: 0 classic (whatever each site draws), 1 crenel, 2 cone, 3 needle spire,
//             4 dome, 5 jagged spikes
//  towerW/H   tower width / height multipliers
//  centre     town and city centrepiece: 0 church, 1 domed temple, 2 totem with standing stones,
//             3 great tree, 4 lone spire tower, 5 none, 6 great hall (a dwelling, scaled up)
//  keep       stronghold of a city, capital, castle or fort: 0 square keep, 1 cluster of towers,
//             2 great hall, 3 great tree, 4 round domed bastion, 5 tower on a rock spire, 6 none,
//             7 single watch tower
//  crag       1: a rock cliff behind the place
//  water      1: shallow water under the place (stilts)
//  dark       1: dark stone and slate (walls shade-filled, roofs inked)
//  ruin       ruins: 0 stone walls, 1 charred timber and stakes (tent poles when house is 3),
//             2 collapsed mounds, 3 broken spires, 4 eroded mudbrick in sand, 5 leaning stilts in
//             water, 6 dead trees with fallen platforms, 7 cracked domes, 8 toppled tower
//  layout     fort plan: 0 walled enclosure, 1 gate cut into a cliff, 2 cluster of towers,
//             3 tower on a rock spire
//  huts       fort: dwellings inside the enclosure

export interface ArchStyle {
  house: number; alt: number; mix: number;
  wall: number; wallH: number;
  towerForm: number; spire: number; towerW: number; towerH: number;
  centre: number; keep: number;
  crag: boolean; water: boolean; dark: boolean;
  ruin: number; layout: number; huts: number;
}

export const WALL_NONE = 5;
const SPIRES: readonly (Roof | null)[] = [null, 'crenel', 'cone', 'needle', 'dome', 'spikes'];

const STYLE_SPEC: ParamSpec = {
  house: { min: 0, max: 8, default: 0 },
  alt: { min: 0, max: 8, default: 0 },
  mix: { min: 0, max: 1, default: 0 },
  wall: { min: 0, max: 7, default: 0 },
  wallH: { min: 0.4, max: 1.6, default: 1 },
  towerForm: { min: 0, max: 3, default: 0 },
  spire: { min: 0, max: 5, default: 0 },
  towerW: { min: 0.5, max: 1.6, default: 1 },
  towerH: { min: 0.5, max: 2, default: 1 },
  centre: { min: 0, max: 6, default: 0 },
  keep: { min: 0, max: 7, default: 0 },
  crag: { min: 0, max: 1, default: 0 },
  water: { min: 0, max: 1, default: 0 },
  dark: { min: 0, max: 1, default: 0 },
};

/** Parameter specs of the styled generators (index.ts registers them). */
export const ARCH_PARAMS = {
  village: { houses: { min: 0, max: 5, default: 0 }, ...STYLE_SPEC, wall: { min: 0, max: 7, default: WALL_NONE } },
  settlement: STYLE_SPEC,
  port: STYLE_SPEC,
  castle: STYLE_SPEC,
  fort: { ...STYLE_SPEC, layout: { min: 0, max: 3, default: 0 }, huts: { min: 0, max: 4, default: 0 } },
  ruins: { ...STYLE_SPEC, ruin: { min: 0, max: 8, default: 0 } },
} satisfies Record<string, ParamSpec>;

const int = (p: Params, k: string, d: number): number => Math.round(p[k] ?? d);

export function archStyle(p: Params): ArchStyle {
  return {
    house: int(p, 'house', 0), alt: int(p, 'alt', 0), mix: p['mix'] ?? 0,
    wall: int(p, 'wall', 0), wallH: p['wallH'] ?? 1,
    towerForm: int(p, 'towerForm', 0), spire: int(p, 'spire', 0), towerW: p['towerW'] ?? 1, towerH: p['towerH'] ?? 1,
    centre: int(p, 'centre', 0), keep: int(p, 'keep', 0),
    crag: (p['crag'] ?? 0) >= 0.5, water: (p['water'] ?? 0) >= 0.5, dark: (p['dark'] ?? 0) >= 0.5,
    ruin: int(p, 'ruin', 0), layout: int(p, 'layout', 0), huts: int(p, 'huts', 0),
  };
}

/** Finishes a styled stamp; `dark` re-inks it as dark stone and slate. */
function done(b: InkBuilder, st: ArchStyle): Stamp {
  const s = b.finish([0, 0]);
  if (!st.dark) return s;
  for (const sh of s.shapes) {
    if (sh.fill === 'wall') sh.fill = 'shade';
    else if (sh.fill === 'roof') sh.fill = 'ink';
    else if (sh.fill === 'shade' && sh.stroke === 'none') { sh.fill = 'ink'; sh.opacity = Math.round((sh.opacity ?? 1) * 35) / 100; }
  }
  return s;
}

/** Cliff and water behind / under a place of half-size (rx, ry). */
function backdrop(b: InkBuilder, r: Rng, st: ArchStyle, rx: number, ry: number, h: number): void {
  if (st.crag) cliff(b, r, -rx - 8, rx + 8, -ry * 0.6, h * r.range(1, 1.4));
  if (st.water) shallows(b, r, 0, 1, rx, ry);
}

// ---------------------------------------------------------------- dwellings

function dwelling(b: InkBuilder, r: Rng, st: ArchStyle, x: number, y: number, s: number): void {
  drawDwelling(b, r, st.mix > 0 && r.chance(st.mix) ? st.alt : st.house, x, y, s);
}

function drawDwelling(b: InkBuilder, r: Rng, kind: number, x: number, y: number, s: number): void {
  switch (kind) {
    case 1: return blockHouse(b, r, x, y, s);
    case 2: return domeHut(b, r, x, y, s);
    case 3: return tent(b, r, x, y, s);
    case 4: return burrow(b, r, x, y, s);
    case 5: return stiltHut(b, r, x, y, s);
    case 6: return longhouse(b, r, x, y, s);
    case 7: return treeHouse(b, r, x, y, s);
    case 8: return pointedHall(b, r, x, y, s);
    default: return house(b, r, x, y, s);
  }
}

/** Oblique box: front face, shaded right side, top face. */
function box(b: InkBuilder, x: number, y: number, w: number, h: number, dx: number, top: PaletteToken): void {
  const dy = dx * 0.45;
  b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
  b.outlined([[x, y], [x, y - h], [x + w, y - h], [x + w, y]], 'wall', ARCH);
  b.outlined([[x, y - h], [x + w, y - h], [x + w + dx, y - h - dy], [x + dx, y - h - dy]], top, ARCH);
}

/** Front half of a ground ellipse, left to right. */
const front = (cx: number, y: number, rx: number, ry: number, n: number): Pt[] => arc(cx, y, rx, ry, Math.PI, 0, n);

function blockHouse(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(4.5, 7), h = s * r.range(3, 4.4), dx = s * r.range(2.4, 3.4);
  box(b, x, y, w, h, dx, 'light');
  b.lines([[[x, y - h + 0.6], [x + w, y - h + 0.6]]], FINE * 0.6, 'ink', 0.6);
  const ink: Pt[][] = [];
  const dxp = x + w * r.range(0.2, 0.55);
  ink.push([[dxp, y], [dxp, y - h * 0.55], [dxp + s, y - h * 0.55], [dxp + s, y]]);
  const wx = dxp > x + w / 2 ? x + w * 0.12 : x + w * 0.72;
  ink.push([[wx, y - h * 0.75], [wx + s * 0.8, y - h * 0.75], [wx + s * 0.8, y - h * 0.5], [wx, y - h * 0.5]]);
  b.ink(ink, false);
  if (r.chance(0.4)) {
    // a smaller upper storey set back on the roof
    const w2 = w * r.range(0.4, 0.6), h2 = h * r.range(0.5, 0.75), x2 = x + dx * 0.5 + r.range(0, w - w2);
    box(b, x2, y - h - dx * 0.22, w2, h2, dx * 0.5, 'light');
    b.ink([[x2 + w2 * 0.4, y - h - dx * 0.22], [x2 + w2 * 0.4, y - h - h2 * 0.5], [x2 + w2 * 0.6, y - h - h2 * 0.5], [x2 + w2 * 0.6, y - h - dx * 0.22]], false);
  }
}

function domeHut(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(4.6, 6), h = s * r.range(2, 2.8), cx = x + w / 2;
  if (r.chance(0.6)) {
    // chimney first: the dome overlaps its foot
    const chx = cx + w * r.range(0.12, 0.28), base = y - h - w * 0.45;
    b.outlined([[chx, base], [chx, base - s * 1.8], [chx + s * 0.8, base - s * 1.8], [chx + s * 0.8, base]], 'wall', ARCH);
    const sx = chx + s * 0.4, sy = base - s * 2.2;
    b.lines([[[sx, sy], [sx - s * 0.5, sy - s * 0.8], [sx + s * 0.2, sy - s * 1.6], [sx - s * 0.3, sy - s * 2.4]]], FINE * 0.8, 'ink', 0.55, true);
  }
  tower(b, r, cx, y, w, h, { roof: 'dome', windows: 0 });
  b.ink([[cx - s * 0.6, y + 0.3], [cx - s * 0.6, y - h * 0.55], ...arc(cx, y - h * 0.55, s * 0.6, s * 0.6, Math.PI, Math.PI * 2, 5), [cx + s * 0.6, y + 0.3]], false);
  b.ink(ellipse(cx + w * 0.3, y - h * 0.55, s * 0.35, s * 0.35, 8), true);
}

function tent(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(5, 6.5), cx = x + w / 2, hw = w / 2;
  if (r.chance(0.5)) {
    // conical hide tent, poles through the smoke hole
    const ap: Pt = [cx + r.jit(0.3), y - s * r.range(5, 6.5)];
    const base = front(cx, y, hw, w * 0.12, 7);
    b.outlined([ap, ...base], 'wall', ARCH);
    b.fill([ap, [cx + hw * 0.15, y + w * 0.11], ...base.filter((p) => p[0] > cx + hw * 0.15)], 'shade', false);
    b.lines([[ap, [cx - hw * 0.45, y + w * 0.1]], [ap, [cx + hw * 0.55, y + w * 0.09]]], FINE * 0.6, 'ink', 0.6);
    b.lines([[ap, [ap[0] - s * 0.9, ap[1] - s * 1.4]], [ap, [ap[0] + s * 0.8, ap[1] - s * 1.5]], [ap, [ap[0] + s * 0.1, ap[1] - s * 1.7]]], 0.5, 'ink');
    b.ink([[cx - s * 0.8, y + w * 0.12], [cx - s * 0.1, y - s * 2.4], [cx + s * 0.5, y + w * 0.12]], false);
  } else {
    // yurt: felt drum under a low cone
    const h = s * r.range(2, 2.6), e = w * 0.14;
    const bottom = front(cx, y, hw, e, 7);
    b.outlined([[cx - hw, y - h], ...bottom, [cx + hw, y - h]], 'wall', ARCH);
    b.fill([[cx + hw * 0.35, y - h], [cx + hw * 0.35, y + e * 0.9], ...bottom.filter((p) => p[0] > cx + hw * 0.35), [cx + hw, y - h]], 'shade', false);
    const lat: Pt[][] = [];
    for (let i = 1; i < 6; i++) { const lx = cx - hw + (w * i) / 6; lat.push([[lx - s * 0.5, y - h * 0.15], [lx + s * 0.5, y - h * 0.85]]); }
    b.lines(lat, FINE * 0.5, 'ink', 0.45);
    const ap: Pt = [cx, y - h - w * r.range(0.35, 0.5)];
    const eave = arc(cx, y - h, hw * 1.08, e, 0, Math.PI, 7);
    b.outlined([ap, [cx + hw * 1.08, y - h], ...eave, [cx - hw * 1.08, y - h]], 'roof', ARCH);
    b.outlined([[ap[0] - s * 0.5, ap[1] + 0.2], [ap[0] - s * 0.4, ap[1] - s * 0.5], [ap[0] + s * 0.4, ap[1] - s * 0.5], [ap[0] + s * 0.5, ap[1] + 0.2]], 'wall', ARCH * 0.8);
    b.ink([[cx - s * 0.6, y + e], [cx - s * 0.6, y - h * 0.65], [cx + s * 0.6, y - h * 0.65], [cx + s * 0.6, y + e]], false);
  }
}

function burrow(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(6, 8), hh = s * r.range(2.8, 3.6), cx = x + w / 2;
  const hill = arc(cx, y, w / 2, hh, Math.PI, Math.PI * 2, 11);
  b.fill(hill, 'foliage', true);
  b.fill(arc(cx, y, w / 2, hh, Math.PI * 1.62, Math.PI * 2, 6).concat([[cx + w * 0.12, y]]), 'foliageDark', true, 0.8);
  b.ink(taper(hill, penProfile(MID * 0.9, 0.2, 0.2, 0.1)));
  const dx = cx + r.jit(w * 0.1), dr = s * r.range(0.95, 1.15);
  b.outlined(ellipse(dx, y - dr * 0.95, dr, dr, 12), 'roof', ARCH, true);
  b.lines([[[dx, y - dr * 1.9], [dx, y]], [[dx + dr * 0.45, y - dr], [dx + dr * 0.45, y - dr * 0.95]]], FINE * 0.7, 'ink', 0.7);
  b.ink(ellipse(dx + (dx > cx ? -1 : 1) * w * 0.27, y - hh * 0.45, s * 0.45, s * 0.45, 8), true);
  const tufts: Pt[][] = [];
  for (let i = 0; i < 3; i++) {
    const t = r.range(0.2, 0.8), p = arc(cx, y, w / 2, hh, Math.PI + Math.PI * t, Math.PI + Math.PI * t, 1)[0]!;
    for (let j = 0; j < 3; j++) tufts.push(blade(r, p[0] + j * 0.6 - 0.6, r.range(1, 1.8), -Math.PI / 2 + (j - 1) * 0.5, 0.4).map(([bx, by]) => [bx, by + p[1]] as Pt));
  }
  b.ink(tufts);
  if (r.chance(0.5)) b.outlined([[cx + w * 0.2, y - hh * 0.8], [cx + w * 0.2, y - hh * 1.25], [cx + w * 0.2 + s * 0.8, y - hh * 1.25], [cx + w * 0.2 + s * 0.8, y - hh * 0.72]], 'wall', ARCH);
}

function stiltHut(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(4, 5), ph = s * r.range(2, 2.8), cx = x + w / 2;
  const posts: Pt[][] = [[[x + 0.4, y + 0.6], [x + 0.4, y - ph]], [[cx, y + 0.9], [cx, y - ph]], [[x + w - 0.4, y + 0.6], [x + w - 0.4, y - ph]]];
  b.lines(posts, 0.8, 'ink');
  b.lines([[[x + 0.4, y - ph * 0.4], [cx, y - ph * 0.9]], [[cx, y - ph * 0.4], [x + w - 0.4, y - ph * 0.9]]], FINE * 0.7, 'ink', 0.7);
  const dk = y - ph;
  b.outlined([[x - 0.6, dk], [x + w + 0.6, dk], [x + w + 0.6, dk - 0.8], [x - 0.6, dk - 0.8]], 'shade', ARCH);
  const bh = s * r.range(1.8, 2.4), by = dk - 0.8;
  b.outlined([[x + 0.3, by], [x + 0.3, by - bh], [x + w - 0.3, by - bh], [x + w - 0.3, by]], 'wall', ARCH);
  const ap: Pt = [cx + r.jit(0.3), by - bh - s * r.range(3, 4)];
  const roof: Pt[] = [[x - 0.9, by - bh + 0.4], ap, [x + w + 0.9, by - bh + 0.4]];
  b.outlined(roof, 'sand', ARCH);
  b.fill([ap, [x + w + 0.9, by - bh + 0.4], [cx + s * 0.3, by - bh + 0.4]], 'ink', false, 0.18);
  const thatch: Pt[][] = [];
  for (let i = 1; i < 5; i++) thatch.push([lerpPt(ap, [x - 0.9 + (w + 1.8) * (i / 5), by - bh + 0.4], 0.35), [x - 0.9 + (w + 1.8) * (i / 5), by - bh + 0.4]]);
  b.lines(thatch, FINE * 0.6, 'ink', 0.6);
  b.ink([[cx - s * 0.5, by], [cx - s * 0.5, by - bh * 0.7], [cx + s * 0.5, by - bh * 0.7], [cx + s * 0.5, by]], false);
  b.lines([[[cx + s * 0.2, y + 0.4], [cx - s * 0.3, dk]], [[cx + s * 0.9, y + 0.4], [cx + s * 0.4, dk]]], FINE, 'ink', 0.8);
}

function longhouse(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(8, 10.5), h = s * r.range(1.6, 2.1), dx = s * r.range(2.2, 3), dy = dx * 0.45, rh = s * r.range(2.4, 3.2);
  const R1: Pt = [x + dx * 0.5, y - h - rh], R2: Pt = [x + w + dx * 0.5, y - h - rh];
  b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
  b.outlined([[x + w, y - h], [x + w + dx, y - h - dy], R2], 'wall', ARCH);
  b.outlined([[x, y], [x, y - h], [x + w, y - h], [x + w, y]], 'wall', ARCH);
  const planks: Pt[][] = [];
  for (let px = x + 1; px < x + w - 0.5; px += 1.1 * s) planks.push([[px, y], [px, y - h]]);
  b.lines(planks, FINE * 0.5, 'ink', 0.5);
  // hogback turf roof: the ridge bows up in the middle
  const ridge = quad(R2, [lerp(R1[0], R2[0], 0.5), R1[1] - s * 0.9], R1, 7);
  b.outlined([[x - 0.6, y - h + 0.3], [x + w + 0.4, y - h + 0.3], ...ridge], 'foliage', ARCH, true);
  b.fill([[x + w * 0.55, y - h + 0.3], [x + w + 0.4, y - h + 0.3], ...ridge.slice(0, 4)], 'foliageDark', true, 0.6);
  const tufts: Pt[][] = [];
  for (let i = 1; i < 5; i++) { const t = i / 5; const p = pointAt(ridge, 1 - t); tufts.push(blade(r, p[0], r.range(0.8, 1.4), -Math.PI / 2 + r.jit(0.4), 0.35).map(([bx, by]) => [bx, by + p[1]] as Pt)); }
  b.ink(tufts);
  // crossed gable finials
  b.lines([[R2, [R2[0] + s * 1.2, R2[1] - s * 1.3]], [[R2[0] + s * 0.7, R2[1] + s * 0.6], [R2[0] - s * 0.4, R2[1] - s * 1.2]]], 0.55, 'ink');
  const dxp = x + w * r.range(0.3, 0.6);
  b.ink([[dxp, y], [dxp, y - h * 0.8], [dxp + s * 1.1, y - h * 0.8], [dxp + s * 1.1, y]], false);
}

function treeHouse(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const cx = x + s * 3.5, th = s * r.range(6, 7.5);
  greatTree(b, r, cx, y, s * 0.75, th);
  // platform hut part way up, in front of the trunk
  const py = y - th * r.range(0.5, 0.62), w = s * 3.4, hx = cx - w / 2 + r.jit(s * 0.6);
  b.outlined([[hx - 0.8, py], [hx + w + 0.8, py], [hx + w + 0.8, py - 0.7], [hx - 0.8, py - 0.7]], 'shade', ARCH);
  const hh = s * 1.6;
  b.outlined([[hx, py - 0.7], [hx, py - 0.7 - hh], [hx + w, py - 0.7 - hh], [hx + w, py - 0.7]], 'wall', ARCH);
  b.outlined([[hx - 0.6, py - 0.7 - hh], [hx + w / 2, py - 0.7 - hh - s * 1.9], [hx + w + 0.6, py - 0.7 - hh]], 'roof', ARCH);
  b.ink([[hx + w * 0.4, py - 0.7], [hx + w * 0.4, py - 0.7 - hh * 0.7], [hx + w * 0.62, py - 0.7 - hh * 0.7], [hx + w * 0.62, py - 0.7]], false);
  b.lines([[[hx + w + 0.3, py], [hx + w + 1.2, y + 0.3]], [[hx + w + 1, py], [hx + w + 1.9, y + 0.3]]], FINE * 0.8, 'ink', 0.8);
}

function pointedHall(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * r.range(3.4, 4.6), h = s * r.range(4, 5.5), rh = s * r.range(5, 7);
  const dx = s * r.range(2, 3), dy = dx * 0.45;
  const A: Pt = [x + w / 2, y - h - rh], E: Pt = [x + w + 0.3, y - h + 0.3];
  b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
  b.outlined([[x, y], [x, y - h], A, [x + w, y - h], [x + w, y]], 'wall', ARCH);
  b.outlined([A, E, [E[0] + dx, E[1] - dy], [A[0] + dx, A[1] - dy]], 'roof', ARCH);
  b.lines([[A, [A[0], A[1] - s * 2.2]], [[A[0] + dx, A[1] - dy], [A[0] + dx, A[1] - dy - s * 1.4]]], 0.5, 'ink');
  const cx = x + w / 2, lw = w * 0.16;
  b.ink([
    [[cx - lw, y], [cx - lw, y - h * 0.3], [cx, y - h * 0.48], [cx + lw, y - h * 0.3], [cx + lw, y]],
    [[cx - lw * 0.7, y - h * 0.62], [cx - lw * 0.7, y - h * 0.85], [cx, y - h - rh * 0.2], [cx + lw * 0.7, y - h * 0.85], [cx + lw * 0.7, y - h * 0.62]],
  ], false);
}

/** Broad tree: trunk of height th standing on (cx, y), crown on top. */
function greatTree(b: InkBuilder, r: Rng, cx: number, y: number, s: number, th: number): void {
  const tw = s * 2;
  b.ink(taper([[cx, y + 0.3], [cx + r.jit(0.4), y - th * 0.5], [cx, y - th]], (t) => lerp(tw, tw * 0.5, t) * (t < 0.12 ? 1 + (0.12 - t) * 5 : 1)));
  b.ink([taper([[cx, y], [cx - s * 2.2, y + 0.6]], penProfile(s * 0.9, 0, 0.8, 0.1)), taper([[cx, y], [cx + s * 2.4, y + 0.5]], penProfile(s * 0.9, 0, 0.8, 0.1))]);
  const rx = s * r.range(5.5, 6.5), ry = s * r.range(4.5, 5.2), cy = y - th - ry * 0.55;
  const crown = lumpy(r, cx, cy, rx, ry, r.int(7, 10), 0.16, 0.85);
  b.fill(crown, 'foliage', false);
  const sh = shadeCrescent(crown, cx, cy, -rx * 0.3, -ry * 0.35, 0.72);
  if (sh.length) b.fill(sh, 'foliageDark', true);
  b.ink(loopStroke(crown, gapStart(crown, cx, cy), 0.08, MID * 1.1));
}

// ---------------------------------------------------------------- towers

/** Tower in the style's form and roof; the classic round stone tower when the style is default. */
function styledTower(b: InkBuilder, r: Rng, st: ArchStyle, cx: number, y: number, w: number, h: number, o: TowerOpts): void {
  const roof = SPIRES[st.spire] ?? o.roof;
  const opts = roof === o.roof ? o : { ...o, roof };
  const tw = w * st.towerW, th = h * st.towerH;
  switch (st.towerForm) {
    case 1: return squareTower(b, r, cx, y, tw, th, opts);
    case 2: return watchPlatform(b, r, cx, y, tw, th, opts);
    case 3: return treePlatform(b, r, cx, y, tw, th, opts);
    default: return tower(b, r, cx, y, tw, th, opts);
  }
}

/** Tall thin cone on an eave ellipse; returns the top y. */
function needleRoof(b: InkBuilder, r: Rng, cx: number, y: number, rx: number, ry: number, height: number, w: number): number {
  const ap: Pt = [cx + r.jit(0.2), y - height];
  const eave = arc(cx, y, rx, ry, 0, Math.PI, 7);
  b.outlined([ap, [cx + rx, y], ...eave, [cx - rx, y]], 'roof', ARCH);
  b.fill([ap, [cx + rx, y], ...eave.filter((p) => p[0] > cx + rx * 0.15), [cx + rx * 0.15, y + ry]], 'ink', false, 0.25);
  b.lines([[ap, [ap[0], ap[1] - w * 0.5]]], 0.45, 'ink');
  return ap[1] - w * 0.5;
}

/** A band from x0 to x1 sitting on y, crowned with jagged spikes; returns the top y. */
function spikeCrown(b: InkBuilder, r: Rng, x0: number, x1: number, y: number, ch: number, spike: number): number {
  b.outlined([[x0, y - ch], [x0, y], [x1, y], [x1, y - ch]], 'wall', ARCH);
  b.fill([[lerp(x0, x1, 0.68), y - ch], [lerp(x0, x1, 0.68), y], [x1, y], [x1, y - ch]], 'shade', false);
  const n = Math.max(3, Math.round((x1 - x0) / 1.6)) | 1;
  const spikes: Pt[][] = [];
  let top = y - ch;
  for (let i = 0; i < n; i++) {
    const a = lerp(x0, x1, i / n), c = lerp(x0, x1, (i + 1) / n);
    const k = i === (n - 1) / 2 ? 1.6 : i % 2 === 0 ? r.range(0.8, 1.1) : r.range(0.45, 0.7);
    const tip: Pt = [(a + c) / 2 + r.jit(0.15), y - ch - spike * k];
    top = Math.min(top, tip[1]);
    spikes.push([[a, y - ch], tip, [c, y - ch]]);
  }
  b.outlined(spikes, 'wall', ARCH);
  return top;
}

function squareTower(b: InkBuilder, r: Rng, cx: number, y: number, w: number, h: number, o: TowerOpts): void {
  const x = cx - w / 2, dx = w * 0.35, dy = dx * 0.45;
  b.outlined([[x + w, y], [x + w, y - h], [x + w + dx, y - h - dy], [x + w + dx, y - dy]], 'shade', ARCH);
  b.outlined([[x, y], [x, y - h], [x + w, y - h], [x + w, y]], 'wall', ARCH);
  const ink: Pt[][] = [];
  const nw = o.windows ?? (h > w * 1.6 ? 2 : 1);
  for (let i = 0; i < nw; i++) {
    const wy = y - h * (0.35 + 0.35 * i), wx = cx + r.jit(w * 0.1);
    ink.push([[wx - w * 0.07, wy], [wx - w * 0.07, wy - w * 0.3], [wx + w * 0.07, wy - w * 0.3], [wx + w * 0.07, wy]]);
  }
  if (ink.length) b.ink(ink, false);
  const T: Pt = [x + w / 2 + dx / 2, y - h - dy / 2];
  let topY = y - h - dy;
  if (o.roof === 'cone' || o.roof === 'needle') {
    const ap: Pt = [T[0], y - h - dy / 2 - w * (o.roof === 'needle' ? r.range(2.6, 3.2) : r.range(1, 1.4))];
    b.outlined([[x - 0.3, y - h], [x + w, y - h], ap], 'roof', ARCH);
    b.outlined([[x + w, y - h], [x + w + dx, y - h - dy], ap], 'roof', ARCH);
    b.fill([[x + w, y - h], [x + w + dx, y - h - dy], ap], 'ink', false, 0.3);
    topY = ap[1];
  } else if (o.roof === 'dome') {
    b.outlined([[x, y - h], [x + w, y - h], [x + w + dx, y - h - dy], [x + dx, y - h - dy]], 'shade', ARCH);
    const dome = arc(T[0], T[1] + dy * 0.3, w * 0.5, w * 0.62, Math.PI, Math.PI * 2, 9);
    b.outlined(dome, 'roof', ARCH, true);
    b.fill(arc(T[0], T[1] + dy * 0.3, w * 0.5, w * 0.62, Math.PI * 1.6, Math.PI * 2, 5).concat([[T[0] + w * 0.15, T[1] + dy * 0.3]]), 'ink', true, 0.22);
    topY = T[1] - w * 0.62;
  } else if (o.roof === 'spikes') {
    topY = spikeCrown(b, r, x - 0.3, x + w + 0.3, y - h, w * 0.22, w * 0.6);
  } else {
    b.outlined([[x, y - h], [x + w, y - h], [x + w + dx, y - h - dy], [x + dx, y - h - dy]], 'shade', ARCH);
    const ch = w * 0.28, nm = Math.max(3, Math.round(w / 1.6)) | 1;
    const merlons: Pt[][] = [];
    for (let i = 0; i < nm; i += 2) {
      const x0 = lerp(x - 0.3, x + w + 0.3, i / nm), x1 = lerp(x - 0.3, x + w + 0.3, (i + 1) / nm);
      merlons.push([[x0, y - h + 0.3], [x0, y - h - ch], [x1, y - h - ch], [x1, y - h + 0.3]]);
    }
    b.outlined(merlons, 'wall', ARCH);
    topY = y - h - ch;
  }
  if (o.flag) flag(b, r, T[0], topY, w * 0.8);
}

/** Timber watch platform on braced legs. */
function watchPlatform(b: InkBuilder, r: Rng, cx: number, y: number, w: number, h: number, o: TowerOpts): void {
  const hw = w * 0.45, py = y - h * 0.72;
  b.lines([[[cx - hw, y + 0.4], [cx - hw * 0.8, py]], [[cx + hw, y + 0.4], [cx + hw * 0.8, py]]], 0.9, 'ink');
  b.lines([[[cx - hw, y], [cx + hw * 0.8, py + 0.5]], [[cx + hw, y], [cx - hw * 0.8, py + 0.5]], [[cx - hw * 0.9, y - h * 0.36], [cx + hw * 0.9, y - h * 0.36]]], FINE * 1.1, 'ink', 0.8);
  const ph = Math.max(1.6, h * 0.22), pw = w * 0.62;
  b.outlined([[cx - pw, py], [cx + pw, py], [cx + pw, py - ph], [cx - pw, py - ph]], 'shade', ARCH);
  const pl: Pt[][] = [];
  for (let i = 1; i < 4; i++) pl.push([[cx - pw + (2 * pw * i) / 4, py], [cx - pw + (2 * pw * i) / 4, py - ph]]);
  b.lines(pl, FINE * 0.6, 'ink', 0.6);
  let topY = py - ph;
  if (o.roof === 'cone' || o.roof === 'needle' || o.roof === 'dome') {
    const rt = py - ph - h * 0.18;
    b.lines([[[cx - pw * 0.85, py - ph], [cx - pw * 0.85, rt]], [[cx + pw * 0.85, py - ph], [cx + pw * 0.85, rt]]], 0.6, 'ink');
    const ap: Pt = [cx, rt - w * (o.roof === 'needle' ? 1.6 : 0.8)];
    b.outlined([[cx - pw * 1.15, rt], [cx + pw * 1.15, rt], ap], 'roof', ARCH);
    b.fill([[cx + pw * 0.2, rt], [cx + pw * 1.15, rt], ap], 'ink', false, 0.25);
    topY = ap[1];
  } else {
    // sharpened stake crown
    const n = Math.max(3, Math.round(pw / 0.7));
    const stakes: Pt[][] = [];
    for (let i = 0; i < n; i++) {
      const a = cx - pw + (2 * pw * i) / n, c = a + (2 * pw) / n;
      stakes.push([[a, py - ph], [a, py - ph - 0.6], [(a + c) / 2, py - ph - 1.6], [c, py - ph - 0.6], [c, py - ph]]);
    }
    b.outlined(stakes, 'shade', ARCH * 0.8);
    topY = py - ph - 1.6;
  }
  if (o.flag) flag(b, r, cx, topY, w * 0.6);
}

/** A tree carrying a lookout platform in its crown. */
function treePlatform(b: InkBuilder, r: Rng, cx: number, y: number, w: number, h: number, o: TowerOpts): void {
  const s = w / 4;
  greatTree(b, r, cx, y, s, h * 0.75);
  const py = y - h * 0.62, pw = w * 0.7;
  b.outlined([[cx - pw, py], [cx + pw, py], [cx + pw, py - 1.2], [cx - pw, py - 1.2]], 'shade', ARCH);
  b.lines([[[cx - pw, py - 1.2], [cx - pw, py - 2.6], [cx + pw, py - 2.6], [cx + pw, py - 1.2]]], 0.5, 'ink');
  b.lines([[[cx - pw * 0.6, py], [cx - pw * 0.2, y]], [[cx - pw * 0.2, py], [cx + pw * 0.2, y]]], FINE * 0.8, 'ink', 0.8);
  if (o.flag) flag(b, r, cx + pw * 0.8, py - 2.6, w * 0.5);
}

// ---------------------------------------------------------------- walls

/** An enclosure along a plan-ellipse arc, in the style's wall type. */
function ring(b: InkBuilder, r: Rng, st: ArchStyle, cx: number, cy: number, rx: number, ry: number, a0: number, a1: number, wh: number, shade: boolean): void {
  const h = wh * st.wallH;
  if (st.wall === 0) return wallArc(b, r, cx, cy, rx, ry, a0, a1, h, shade);
  if (st.wall === WALL_NONE) return;
  if (st.wall === 6) { wallArc(b, r, cx, cy, rx, ry, a0, a1, h, shade); return spikesAlong(b, r, cx, cy, rx, ry, a0, a1, h); }
  const n = Math.max(4, Math.round((Math.abs(a1 - a0) * rx) / 1.4));
  const ground = arc(cx, cy, rx, ry, a0, a1, n);
  switch (st.wall) {
    case 1: return palisade(b, r, ground, h * 1.1);
    case 2: return hedge(b, r, ground, h);
    case 4: return reedFence(b, r, ground, h);
    default: return band(b, r, ground, cx + rx * 0.45, h, st.wall === 3 ? 'sand' : 'wall', st.wall === 3 ? 1.8 : 4.5, shade);
  }
}

function palisade(b: InkBuilder, r: Rng, ground: Pt[], h: number): void {
  const L = polyLength(ground), k = Math.max(3, Math.round(L / 1.25));
  const stakes: Pt[][] = [];
  for (let i = 0; i <= k; i++) {
    const [px, py] = pointAt(ground, i / k), sh = h * r.range(0.9, 1.2);
    stakes.push([[px - 0.6, py + 0.2], [px - 0.6, py - sh], [px + r.jit(0.1), py - sh - 1], [px + 0.6, py - sh], [px + 0.6, py + 0.2]]);
  }
  b.outlined(stakes, 'shade', ARCH * 0.8);
  b.lines([ground.map(([x, y]) => [x, y - h * 0.3] as Pt), ground.map(([x, y]) => [x, y - h * 0.75] as Pt)], FINE, 'ink', 0.8);
}

function hedge(b: InkBuilder, r: Rng, ground: Pt[], h: number): void {
  const top = ground.map(([x, y], i) => [x, y - h * (0.85 + 0.25 * Math.abs(Math.sin(i * 1.7)) + r.jit(0.08))] as Pt);
  b.fill(ground.concat(top.slice().reverse()), 'foliage', true);
  b.fill(ground.concat(ground.map(([x, y]) => [x, y - h * 0.4] as Pt).reverse()), 'foliageDark', true, 0.7);
  b.ink(taper(top, penProfile(MID * 0.8, 0.1, 0.1, 0.2)), true);
  const thorns: Pt[][] = [];
  for (let i = 1; i < top.length - 1; i += 2) { const [x, y] = top[i]!; thorns.push([[x, y + h * 0.3], [x + r.jit(0.6), y + h * 0.05]]); }
  b.lines(thorns, FINE * 0.8, 'ink', 0.7);
  b.lines([ground], FINE, 'ink', 0.8);
}

function reedFence(b: InkBuilder, r: Rng, ground: Pt[], h: number): void {
  const L = polyLength(ground), k = Math.max(4, Math.round(L / 0.55));
  const reeds: Pt[][] = [];
  for (let i = 0; i <= k; i++) {
    const [px, py] = pointAt(ground, i / k), rh = h * r.range(0.8, 1.3);
    reeds.push([[px, py + 0.2], [px + r.jit(0.25), py - rh]]);
  }
  b.fill(ground.concat(ground.map(([x, y]) => [x, y - h * 0.9] as Pt).reverse()), 'sand', true, 0.7);
  b.lines(reeds, FINE * 1.1, 'ink', 0.85);
  b.lines([ground.map(([x, y]) => [x, y - h * 0.5] as Pt)], FINE * 1.2, 'ink');
}

/** Solid wall band with a crown of rounded merlons every `step` units. */
function band(b: InkBuilder, r: Rng, ground: Pt[], shadeX: number, h: number, fill: PaletteToken, step: number, shade: boolean): void {
  const top = ground.map(([x, y]) => [x, y - h] as Pt);
  const crown: Pt[] = [];
  const L = polyLength(top), k = Math.max(2, Math.round(L / step));
  for (let i = 0; i < k; i++) {
    const p = pointAt(top, i / k), q = pointAt(top, (i + 1) / k), m = lerpPt(p, q, 0.5);
    crown.push(p, [lerp(p[0], m[0], 0.35), p[1] - h * 0.12], [m[0], m[1] - h * 0.3], [lerp(m[0], q[0], 0.65), q[1] - h * 0.12]);
  }
  crown.push(top[top.length - 1]!);
  b.fill(ground.concat(crown.slice().reverse()), fill, false);
  if (shade) {
    const seg = ground.filter((p) => p[0] > shadeX);
    if (seg.length >= 2) b.fill(seg.concat(seg.map(([x, y]) => [x, y - h] as Pt).reverse()), 'shade', false, 0.8);
  }
  b.lines([jitter(ground, r, 0.12), crown], ARCH, 'ink');
  b.lines([[ground[0]!, top[0]!], [ground[ground.length - 1]!, top[top.length - 1]!]], ARCH, 'ink');
}

function spikesAlong(b: InkBuilder, r: Rng, cx: number, cy: number, rx: number, ry: number, a0: number, a1: number, h: number): void {
  const top = arc(cx, cy, rx, ry, a0, a1, Math.max(4, Math.round((Math.abs(a1 - a0) * rx) / 3))).map(([x, y]) => [x, y - h * 1.35] as Pt);
  const L = polyLength(top), k = Math.max(3, Math.round(L / 2.2));
  const spikes: Pt[][] = [];
  for (let i = 0; i <= k; i++) {
    const [x, y] = pointAt(top, i / k);
    spikes.push([[x - 0.45, y + 0.4], [x + r.jit(0.15), y - h * r.range(0.35, 0.55)], [x + 0.45, y + 0.4]]);
  }
  b.outlined(spikes, 'wall', ARCH * 0.8);
}

// ---------------------------------------------------------------- terrain under a place

/** A rock cliff standing on y from x0 to x1, up to h tall, highest in the middle. */
function cliff(b: InkBuilder, r: Rng, x0: number, x1: number, y: number, h: number): void {
  const n = Math.max(5, Math.round((x1 - x0) / 5));
  const top: Pt[] = [];
  for (let i = 1; i < n; i++) {
    const t = i / n;
    top.push([lerp(x0, x1, t) + r.jit((x1 - x0) / n * 0.3), y - h * Math.pow(Math.sin(Math.PI * t), 0.55) * r.range(0.7, 1)]);
  }
  const poly: Pt[] = [[x0, y], ...top, [x1, y]];
  b.fill(poly, 'paper', false);
  const facets: Pt[][] = [];
  for (let i = 0; i + 1 < top.length; i++) {
    const p = top[i]!, q = top[i + 1]!;
    if (q[1] > p[1]) facets.push([p, q, [lerp(p[0], q[0], 0.3), lerp(p[1], y, 0.75)]]);
  }
  b.fill(facets, 'shade', false);
  b.ink(taper(poly, penProfile(OUT * 0.7, 0.12, 0.12, 0.05)), false);
  const cracks: Pt[][] = [];
  for (const p of top) if (r.chance(0.6)) cracks.push([p, [p[0] + r.jit(1), lerp(p[1], y, r.range(0.3, 0.55))]]);
  b.lines(cracks, FINE * 1.1, 'ink', 0.7);
}

/** A rock pillar standing on (cx, y); returns the y of its top. */
function rockSpire(b: InkBuilder, r: Rng, cx: number, y: number, w: number, h: number): number {
  const hw = w / 2, top = y - h;
  const left: Pt[] = [[cx - hw, y], [cx - hw * 0.75 + r.jit(0.5), y - h * 0.4], [cx - hw * 0.55, y - h * 0.75], [cx - hw * 0.5, top]];
  const right: Pt[] = [[cx + hw * 0.5, top], [cx + hw * 0.6, y - h * 0.7], [cx + hw * 0.8 + r.jit(0.5), y - h * 0.35], [cx + hw, y]];
  const poly = left.concat(right);
  b.fill(poly, 'paper', false);
  b.fill([[cx + hw * 0.1, top], ...right, [cx + hw * 0.2, y]], 'shade', false);
  b.ink(taper(poly, penProfile(OUT * 0.65, 0.1, 0.1, 0.05)), false);
  const hatch: Pt[][] = [];
  for (let i = 1; i < 4; i++) { const yy = lerp(top, y, i / 4); hatch.push([[cx + hw * 0.3, yy], [cx + hw * 0.65, yy + h * 0.08]]); }
  b.lines(hatch, FINE * 0.8, 'ink', 0.7);
  return top;
}

/** Shallow water under a place: a tinted patch and ripple lines. */
function shallows(b: InkBuilder, r: Rng, cx: number, cy: number, rx: number, ry: number): void {
  b.fill(ellipse(cx, cy, rx, ry, 18), 'water', true, 0.55);
  const lines: Pt[][] = [];
  for (let k = 0; k < 4; k++) {
    const t = (k + 0.5) / 4, yy = cy - ry * 0.7 + ry * 1.4 * t, half = rx * Math.sqrt(Math.max(0, 1 - ((yy - cy) / ry) ** 2)) * 0.8;
    if (half > 3) lines.push(...waterLines(r, cx - half, cx + half, yy, 1));
  }
  b.lines(lines, FINE, 'ink', 0.7);
}

// ---------------------------------------------------------------- centrepieces and strongholds

/** A town or city's centrepiece with its front-left corner at (x, y). */
function centrepiece(b: InkBuilder, r: Rng, st: ArchStyle, x: number, y: number, s: number): void {
  switch (st.centre) {
    case 1: return domeTemple(b, r, x, y, s);
    case 2: return totem(b, r, x + s * 3, y, s);
    case 3: return greatTree(b, r, x + s * 4, y, s * 1.2, s * 7);
    case 4: return styledTower(b, r, st, x + s * 4, y, s * 4.5, s * 13, { roof: 'cone', windows: 2, flag: true });
    case 5: return;
    case 6: return drawDwelling(b, r, st.house, x, y, s * 1.8);
    default: return church(b, r, x, y, s);
  }
}

function domeTemple(b: InkBuilder, r: Rng, x: number, y: number, s: number): void {
  const w = s * 8, h = s * 3.6, dx = s * 3;
  box(b, x, y, w, h, dx, 'shade');
  const cols: Pt[][] = [];
  for (let i = 1; i < 6; i++) cols.push([[x + (w * i) / 6, y], [x + (w * i) / 6, y - h * 0.8]]);
  b.lines(cols, FINE * 0.7, 'ink', 0.7);
  b.ink([[x + w * 0.42, y], [x + w * 0.42, y - h * 0.45], ...arc(x + w * 0.5, y - h * 0.45, w * 0.08, w * 0.08, Math.PI, Math.PI * 2, 4), [x + w * 0.58, y]], false);
  const dcx = x + w / 2 + dx * 0.45, dbase = y - h - dx * 0.2, dr = w * r.range(0.28, 0.34);
  b.outlined([[dcx - dr, dbase], [dcx + dr, dbase], [dcx + dr, dbase - s * 1.6], [dcx - dr, dbase - s * 1.6]], 'wall', ARCH);
  const dome = arc(dcx, dbase - s * 1.6, dr, dr * 0.95, Math.PI, Math.PI * 2, 11);
  b.outlined(dome, 'roof', ARCH, true);
  b.fill(arc(dcx, dbase - s * 1.6, dr, dr * 0.95, Math.PI * 1.62, Math.PI * 2, 6).concat([[dcx + dr * 0.25, dbase - s * 1.6]]), 'ink', true, 0.22);
  b.lines([[[dcx, dbase - s * 1.6 - dr * 0.95], [dcx, dbase - s * 1.6 - dr * 0.95 - s * 2.2]]], 0.6, 'ink');
}

function totem(b: InkBuilder, r: Rng, cx: number, y: number, s: number): void {
  // standing stones behind and beside
  for (const [ox, oy, k] of [[-s * 4.5, -s * 1.2, 1], [s * 4.8, -s * 0.8, 0.85], [-s * 2.6, s * 1.4, 0.75]] as const) {
    const sw = s * 1.3 * k, sh = s * r.range(3.5, 4.5) * k, x = cx + ox, yy = y + oy;
    b.outlined([[x - sw, yy], [x - sw * 0.85, yy - sh], [x - sw * 0.2, yy - sh - sw * 0.5], [x + sw * 0.8, yy - sh * 0.95], [x + sw, yy]], 'paper', ARCH);
    b.fill([[x + sw * 0.25, yy], [x + sw * 0.2, yy - sh * 1.02], [x + sw * 0.8, yy - sh * 0.95], [x + sw, yy]], 'shade', false);
  }
  const pw = s * 0.75, ph = s * r.range(10, 13);
  b.outlined([[cx - pw, y], [cx - pw, y - ph], [cx + pw, y - ph], [cx + pw, y]], 'roof', ARCH);
  const carv: Pt[][] = [];
  for (let i = 1; i < 4; i++) carv.push([[cx - pw, y - (ph * i) / 4], [cx + pw, y - (ph * i) / 4]]);
  b.lines(carv, FINE * 0.8, 'ink', 0.8);
  const eyes: Pt[][] = [];
  for (let i = 0; i < 3; i++) { const ey = y - ph * (0.15 + i * 0.25); eyes.push(ellipse(cx - pw * 0.4, ey, 0.25, 0.25, 6), ellipse(cx + pw * 0.4, ey, 0.25, 0.25, 6)); }
  b.ink(eyes, true);
  const wy = y - ph * 0.9;
  b.outlined([[cx - pw, wy], [cx - pw * 4.5, wy - s * 1.6], [cx - pw * 3.8, wy + s * 0.4], [cx - pw, wy + s * 0.6]], 'wall', ARCH);
  b.outlined([[cx + pw, wy], [cx + pw * 4.5, wy - s * 1.6], [cx + pw * 3.8, wy + s * 0.4], [cx + pw, wy + s * 0.6]], 'wall', ARCH);
  b.outlined([[cx - pw, y - ph], [cx, y - ph - s * 1.8], [cx + pw, y - ph]], 'roof', ARCH);
}

/** A city's, castle's or fort's stronghold, front-left corner at (x, y), w wide and h tall. */
function stronghold(b: InkBuilder, r: Rng, st: ArchStyle, x: number, y: number, w: number, h: number, capital: boolean): void {
  switch (st.keep) {
    case 1: {
      const n = capital ? 5 : 3, mid = (n - 1) / 2;
      const ts = Array.from({ length: n }, (_, i) => ({ i, dy: i === mid ? -1.2 : (i % 2) * -2 + r.jit(0.5) }));
      ts.sort((a, c) => a.dy - c.dy);
      for (const t of ts) {
        const centre = t.i === mid;
        styledTower(b, r, st, x + (w * (t.i + 0.5)) / n, y + t.dy, w * (centre ? 0.42 : 0.32), h * (centre ? 1.5 : r.range(0.85, 1.15)), { roof: 'cone', windows: 2, flag: centre });
      }
      return;
    }
    case 2: return drawDwelling(b, r, st.house, x, y, w / 5);
    case 3: return greatTree(b, r, x + w / 2, y, w / 6, h * 0.5);
    case 4: {
      for (const s of [-1, 1]) styledTower(b, r, st, x + w / 2 + s * w * 0.55, y - 1, w * 0.3, h * 0.45, { roof: 'dome', windows: 1 });
      tower(b, r, x + w / 2, y, w * 1.05, h * 0.5, { roof: 'dome', windows: 2, flag: capital || r.chance(0.5) });
      return;
    }
    case 5: {
      const top = rockSpire(b, r, x + w / 2, y, w * 0.9, h * 0.75);
      return styledTower(b, r, st, x + w / 2, top + 0.6, w * 0.45, h * 0.75, { roof: 'cone', windows: 2, flag: true });
    }
    case 6: return;
    case 7: return styledTower(b, r, st, x + w / 2, y, w * 0.55, h * 0.9, { roof: 'cone', windows: 1, flag: true });
    default: return keep(b, r, st, x, y, w, h, capital);
  }
}

// ---------------------------------------------------------------- forts

export function genFort(r: Rng, p: Params): Stamp {
  const b = new InkBuilder();
  const st = archStyle(p);
  if (st.crag && st.layout !== 1) cliff(b, r, -28, 28, -5, r.range(14, 19));
  if (st.water) shallows(b, r, 0, 1, 22, 8);
  switch (st.layout) {
    case 1: fortCliffGate(b, r, st); break;
    case 2: fortTowers(b, r, st); break;
    case 3: {
      drawBoulder(b, r, -7, 1.2, r.range(2.2, 3), MID);
      const top = rockSpire(b, r, 0, 0, r.range(10, 12), r.range(10, 14));
      styledTower(b, r, st, 0, top + 0.6, 6, r.range(12, 16), { roof: 'cone', windows: 2, flag: r.chance(0.6) });
      const steps: Pt[] = [];
      for (let i = 0; i <= 4; i++) steps.push([(i % 2 ? 2.5 : -2.5) + r.jit(0.4), lerp(0, top + 1, i / 4)]);
      b.lines([steps], FINE, 'ink', 0.7);
      drawBoulder(b, r, 7, 1.8, r.range(2, 2.6), MID);
      break;
    }
    default: fortEnclosure(b, r, st);
  }
  b.ink([groundDash(r, -22, -12, 4.5, MID * 0.7), groundDash(r, 11, 22, 4.8, MID * 0.7)]);
  return done(b, st);
}

function fortEnclosure(b: InkBuilder, r: Rng, st: ArchStyle): void {
  const R = r.range(13, 16), ry = R * 0.42, wh = 4;
  b.fill(ellipse(0, 0, R + 1, ry + 1, 16).map(([x, y]) => [x, y < 0 ? y - wh : y] as Pt), 'paper');
  ring(b, r, st, 0, 0, R, ry, Math.PI, Math.PI * 2, wh, false);
  for (const a of [1.2, 1.8]) {
    const tp = arc(0, 0, R, ry, a * Math.PI, a * Math.PI, 1)[0]!;
    styledTower(b, r, st, tp[0], tp[1] + 0.5, 4.2, 6.5, { roof: 'crenel', windows: 0 });
  }
  const items: Array<{ y: number; draw: () => void }> = [];
  for (const q of scatter(r, st.huts, R * 0.6, ry * 0.45, 6, 0, 0)) items.push({ y: q[1], draw: () => dwelling(b, r, st, q[0] - 3, q[1], r.range(0.9, 1.05)) });
  const kx = r.jit(2) - 4, kw = r.range(7, 9), kh = r.range(10, 13);
  items.push({ y: -ry * 0.15, draw: () => stronghold(b, r, st, kx, -ry * 0.15, kw, kh, false) });
  items.sort((a, c) => a.y - c.y);
  for (const it of items) it.draw();
  ring(b, r, st, 0, 0, R, ry, 0, Math.PI, wh, true);
  if (st.wall !== WALL_NONE) b.ink([[-1.4, ry + 0.3], [-1.4, ry - 2.6], [0, ry - 3.4], [1.4, ry - 2.6], [1.4, ry + 0.3]], true);
  for (const t of [0, 1]) {
    const tp = arc(0, 0, R, ry, t * Math.PI, t * Math.PI, 1)[0]!;
    styledTower(b, r, st, tp[0], tp[1] + 0.5, 5.2, 8.5, { roof: r.chance(0.4) ? 'cone' : 'crenel', windows: 1, flag: r.chance(0.3) });
  }
}

function fortCliffGate(b: InkBuilder, r: Rng, st: ArchStyle): void {
  const H = r.range(20, 26);
  cliff(b, r, -32, 32, 0, H);
  // the carved gatehouse front
  const fw = r.range(6.5, 8), fh = H * r.range(0.5, 0.6);
  b.outlined([[-fw, 0.2], [-fw, -fh], [fw, -fh], [fw, 0.2]], 'wall', ARCH);
  const courses: Pt[][] = [];
  for (let yy = -2.2; yy > -fh + 1; yy -= 2.2) courses.push([[-fw + 0.3, yy], [fw - 0.3, yy]]);
  b.lines(courses, FINE * 0.6, 'ink', 0.5);
  b.outlined([[-fw - 1, -fh], [fw + 1, -fh], [fw + 1, -fh - 1.6], [-fw - 1, -fh - 1.6]], 'shade', ARCH);
  const gw = fw * 0.45, gh = fh * 0.55;
  b.ink([[-gw, 0.3], [-gw, -gh], ...arc(0, -gh, gw, gw * 0.8, Math.PI, Math.PI * 2, 7), [gw, 0.3]], false);
  b.lines([[[-gw * 0.33, 0.3], [-gw * 0.33, -gh - gw * 0.5]], [[gw * 0.33, 0.3], [gw * 0.33, -gh - gw * 0.5]]], FINE, 'paper', 0.6);
  ring(b, r, st, 0, 0, fw + 12, 4.5, 0.08 * Math.PI, 0.32 * Math.PI, 3.5, true);
  ring(b, r, st, 0, 0, fw + 12, 4.5, 0.68 * Math.PI, 0.92 * Math.PI, 3.5, true);
  for (const s of [-1, 1]) styledTower(b, r, st, s * (fw + 3), 1.2, 5.5, r.range(10, 12), { roof: 'crenel', windows: 2, flag: s === 1 && r.chance(0.6) });
}

function fortTowers(b: InkBuilder, r: Rng, st: ArchStyle): void {
  ring(b, r, st, 0, 0, 13, 5, Math.PI, Math.PI * 2, 2.6, false);
  const n = r.int(3, 5);
  const ts = Array.from({ length: n }, (_, i) => ({ x: lerp(-8, 8, n === 1 ? 0.5 : i / (n - 1)) + r.jit(1), y: r.range(-3, 1.5), h: r.range(11, 17) }));
  const tall = Math.floor(n / 2);
  ts[tall]!.h = r.range(19, 24);
  ts.sort((a, c) => a.y - c.y);
  for (const t of ts) styledTower(b, r, st, t.x, t.y, t.h > 18 ? 5 : 3.8, t.h, { roof: 'cone', windows: 2, flag: t.h > 18 });
  ring(b, r, st, 0, 0, 13, 5, 0, Math.PI, 2.6, true);
}

// ---------------------------------------------------------------- styled ruins

function styledRuins(b: InkBuilder, r: Rng, st: ArchStyle): void {
  let span = 14;
  switch (st.ruin) {
    case 1: span = charredRuins(b, r, st.house === 3); break;
    case 2: span = moundRuins(b, r); break;
    case 3: span = spireRuins(b, r); break;
    case 4: span = sandRuins(b, r); break;
    case 5: span = stiltRuins(b, r, st.water); break;
    case 6: span = treeRuins(b, r); break;
    case 7: span = domeRuins(b, r); break;
    default: span = toppledTower(b, r); break;
  }
  const ink: Pt[][] = [groundDash(r, -span - 4, span + 4, 3.8, MID * 0.7)];
  for (let i = 0; i < 2; i++) {
    const gx = r.range(-span, span);
    for (let j = 0; j < 3; j++) ink.push(blade(r, gx + j * 0.8 - 0.8, r.range(2, 3.5), -Math.PI / 2 + (j - 1) * 0.5, 0.5).map(([x, y]) => [x, y + 3] as Pt));
  }
  b.ink(ink);
}

/** Small tumbled blocks scattered on the ground. */
function rubble(b: InkBuilder, r: Rng, span: number, fill: PaletteToken): void {
  const rb: Pt[][] = [];
  for (let i = 0; i < r.int(3, 6); i++) {
    const x = r.range(-span, span), y = r.range(1, 3.5), s = r.range(0.8, 1.5), a = r.jit(0.5);
    rb.push([[0, 0], [s * 1.6, 0], [s * 1.6, -s], [0, -s]].map(([px, py]) => [x + px * Math.cos(a) - py * Math.sin(a), y + px * Math.sin(a) + py * Math.cos(a)] as Pt));
  }
  b.outlined(rb, fill, ARCH);
}

function charredRuins(b: InkBuilder, r: Rng, poles: boolean): number {
  const n = r.int(3, 4), span = n * 4.5;
  b.fill(ellipse(0, 1.5, span + 3, 3.2, 14), 'shade', true, 0.55);
  const ink: Pt[][] = [];
  for (let i = 0; i < n; i++) {
    const x = (i - (n - 1) / 2) * 8 + r.jit(1.5), y = r.range(-2, 1.5);
    if (poles) {
      // a tent's bare pole frame, hides burnt away
      const ap: Pt = [x + r.jit(1), y - r.range(7, 10)];
      for (const fx of [-2.6, 2.4, 0.4]) {
        const foot: Pt = [x + fx, y + (fx === 0.4 ? 1.4 : 0.6)];
        const end = r.chance(0.3) ? lerpPt(foot, ap, r.range(0.55, 0.8)) : [ap[0] + (ap[0] - foot[0]) * 0.18, ap[1] - 1.2] as Pt;
        ink.push(taper([foot, end], penProfile(0.9, 0.05, 0.3, 0.3)));
      }
      b.outlined([lerpPt([x - 2.6, y + 0.6], ap, 0.45), lerpPt([x + 2.4, y + 0.6], ap, 0.45), lerpPt([x + 2.4, y + 0.6], ap, 0.25), [x - 0.2, y - 0.5]], 'wall', ARCH * 0.8);
    } else {
      // a charred post-and-beam frame with broken tops
      const h1 = r.range(5, 9), h2 = r.range(4, 8), w = r.range(4, 6);
      ink.push(taper([[x, y + 0.4], [x + r.jit(0.6), y - h1]], penProfile(1.2, 0, 0.5, 0.35)));
      ink.push(taper([[x + w, y + 0.4], [x + w + r.jit(0.6), y - h2]], penProfile(1.2, 0, 0.5, 0.35)));
      const by = y - Math.min(h1, h2) * r.range(0.6, 0.85);
      ink.push(taper([[x - 0.5, by + r.jit(0.4)], [x + w + 0.6, by + r.jit(0.8)]], penProfile(0.9, 0.1, 0.1, 0.3)));
      if (r.chance(0.6)) ink.push(taper([[x + w * 0.5, by], [x + w * 0.5 + r.jit(1), by - r.range(2, 4)]], penProfile(0.8, 0.05, 0.4, 0.3)));
    }
  }
  const k = r.int(5, 8);
  for (let i = 0; i < k; i++) {
    const x = lerp(-span, span, (i + 0.5) / k) + r.jit(0.6), h = r.range(1.5, 3.5);
    ink.push([[x - 0.45, 3.2], [x - 0.45, 3.2 - h], [x + r.jit(0.2), 3.2 - h - 0.9], [x + 0.45, 3.2 - h], [x + 0.45, 3.2]]);
  }
  ink.push(taper([[-span * 0.5, 2.2], [span * 0.1, 2.9]], penProfile(1, 0.1, 0.1, 0.3)));
  b.ink(ink, false);
  return span;
}

function moundRuins(b: InkBuilder, r: Rng): number {
  const n = r.int(2, 3), span = n * 5;
  for (let i = 0; i < n; i++) {
    const cx = (i - (n - 1) / 2) * 9 + r.jit(1.2), y = r.range(-1, 1.5), w = r.range(7, 10), h = r.range(2, 3);
    const hill = arc(cx, y, w / 2, h, Math.PI, Math.PI * 2, 11);
    b.fill(hill, 'foliage', true);
    b.ink(taper(hill, penProfile(MID * 0.8, 0.2, 0.2, 0.1)));
    // caved-in roof and a half-buried round door frame
    b.fill(ellipse(cx + r.jit(1), y - h * 0.65, w * 0.22, h * 0.25, 10), 'ink', true, 0.55);
    const dx = cx + r.jit(w * 0.2);
    b.lines([arc(dx, y, 1.1, 1.1, Math.PI * 1.05, Math.PI * r.range(1.5, 1.9), 6)], 0.7, 'ink');
  }
  rubble(b, r, span, 'paper');
  return span;
}

/** A broken round shaft on (cx, y), jagged at the top. */
function stump(b: InkBuilder, r: Rng, cx: number, y: number, w: number, h: number): void {
  const hw = w / 2;
  const body: Pt[] = [[cx - hw, y], [cx - hw, y - h * r.range(0.7, 0.85)], [cx - hw * 0.3, y - h], [cx + hw * 0.1, y - h * r.range(0.8, 0.9)], [cx + hw * 0.5, y - h * r.range(0.9, 0.97)], [cx + hw, y - h * r.range(0.55, 0.7)], [cx + hw, y]];
  b.outlined(body, 'wall', ARCH);
  b.fill([[cx + hw * 0.35, y], [cx + hw * 0.35, y - h * 0.8], [cx + hw * 0.5, y - h * 0.85], [cx + hw, y - h * 0.6], [cx + hw, y]], 'shade', false);
  b.lines([body], ARCH, 'ink');
  b.ink([[cx - w * 0.08, y - h * 0.4], [cx - w * 0.08, y - h * 0.55], [cx + w * 0.08, y - h * 0.55], [cx + w * 0.08, y - h * 0.4]], false);
}

function spireRuins(b: InkBuilder, r: Rng): number {
  const n = r.int(2, 4), span = n * 4;
  const ts = Array.from({ length: n }, (_, i) => ({ x: (i - (n - 1) / 2) * 6.5 + r.jit(1.2), y: r.range(-2.5, 1.5), h: r.range(6, 14), w: r.range(2.6, 3.6) }));
  ts.sort((a, c) => a.y - c.y);
  for (const t of ts) stump(b, r, t.x, t.y, t.w, t.h);
  // a fallen spire tip
  const fx = r.range(-span, span * 0.3), len = r.range(7, 10);
  b.outlined([[fx, 2.8], [fx + len, 1.8 + r.jit(0.5)], [fx + 0.3, 0.6]], 'roof', ARCH);
  b.fill([[fx, 2.8], [fx + len, 1.8], [fx + 0.2, 1.9]], 'ink', false, 0.3);
  rubble(b, r, span, 'wall');
  return span;
}

function sandRuins(b: InkBuilder, r: Rng): number {
  const n = r.int(2, 4), span = n * 4.5;
  const ps = Array.from({ length: n }, (_, i) => ({ x: (i - n / 2) * 8 + r.jit(2), y: r.range(-2.5, 1), w: r.range(5, 9), h: r.range(4, 9) }));
  ps.sort((a, c) => a.y - c.y);
  for (const p of ps) {
    // eroded block: the top worn into soft humps
    const top = quad([p.x + p.w, p.y - p.h * r.range(0.4, 0.7)], [p.x + p.w * 0.5, p.y - p.h * r.range(1, 1.25)], [p.x, p.y - p.h * r.range(0.5, 0.8)], 6);
    b.outlined([[p.x + p.w, p.y], [p.x + p.w + 2, p.y - 0.9], [p.x + p.w + 2, top[0]![1] - 0.9], [p.x + p.w, top[0]![1]]], 'shade', ARCH);
    b.outlined([[p.x, p.y], [p.x + p.w, p.y], ...top], 'sand', ARCH, true);
    if (p.w > 6) b.ink([[p.x + p.w * 0.4, p.y], [p.x + p.w * 0.4, p.y - p.h * 0.35], [p.x + p.w * 0.6, p.y - p.h * 0.35], [p.x + p.w * 0.6, p.y]], false, 0.85);
  }
  // drifted sand burying the feet
  const drift: Pt[] = [];
  for (let i = 0; i <= 8; i++) drift.push([lerp(-span - 6, span + 6, i / 8), r.range(-1.2, 0.8) + (i % 2 ? -0.6 : 0.4)]);
  b.fill(drift.concat([[span + 6, 4], [-span - 6, 4]]), 'sand', true);
  b.ink(taper(drift, penProfile(MID * 0.8, 0.3, 0.3, 0.05)), true);
  const ripples: Pt[][] = [];
  for (let i = 0; i < 3; i++) { const x = r.range(-span, span); ripples.push([[x, 2.4 + i * 0.3], [x + 2, 2 + i * 0.3], [x + 4, 2.4 + i * 0.3]]); }
  b.lines(ripples, FINE * 0.7, 'ink', 0.6, true);
  return span;
}

function stiltRuins(b: InkBuilder, r: Rng, hasWater: boolean): number {
  const span = 14;
  if (!hasWater) shallows(b, r, 0, 2.5, 20, 5);
  const posts: Pt[][] = [];
  const k = r.int(5, 8);
  for (let i = 0; i < k; i++) {
    const x = lerp(-span, span, (i + 0.5) / k) + r.jit(1), y = r.range(1, 3), len = r.range(4, 9), a = -Math.PI / 2 + r.jit(0.4);
    posts.push([[x, y], [x + Math.cos(a) * len, y + Math.sin(a) * len]]);
  }
  b.lines(posts, 0.9, 'ink');
  // a tilted deck slab and a collapsed thatch cone
  const dx = r.range(-8, 2), ang = r.jit(0.25), dw = r.range(6, 9);
  const deck: Pt[] = [[0, 0], [dw, 0], [dw, -0.9], [0, -0.9]].map(([px, py]) => [dx + px * Math.cos(ang) - py * Math.sin(ang), -2.5 + px * Math.sin(ang) + py * Math.cos(ang)] as Pt);
  b.outlined(deck, 'shade', ARCH);
  const cx = r.range(2, 9);
  b.outlined([[cx - 3.5, 2.6], [cx + 3.6, 1.8], [cx + r.jit(1), -1.4]], 'sand', ARCH);
  b.lines([[[cx - 1.5, 2.3], [cx + 0.2, -0.8]], [[cx + 1.6, 2], [cx + 0.6, -0.6]]], FINE * 0.6, 'ink', 0.6);
  return span;
}

function treeRuins(b: InkBuilder, r: Rng): number {
  const n = r.int(2, 3), span = n * 5;
  const ink: Pt[][] = [];
  const branch = (p: Pt, ang: number, len: number, w: number, depth: number): void => {
    const e: Pt = [p[0] + Math.cos(ang) * len, p[1] + Math.sin(ang) * len];
    ink.push(taper([p, lerpPt(p, e, 0.5), e], penProfile(w, 0, 0.75, depth === 0 ? 0.05 : 0.3)));
    if (depth <= 0) return;
    for (let i = 0; i < 2; i++) branch(lerpPt(p, e, r.range(0.65, 1)), ang + (i === 0 ? -1 : 1) * r.range(0.35, 0.7), len * r.range(0.5, 0.7), w * 0.62, depth - 1);
  };
  for (let i = 0; i < n; i++) branch([(i - (n - 1) / 2) * 10 + r.jit(1.5), r.range(-1, 1)], -Math.PI / 2 + r.jit(0.2), r.range(6, 9), 2.2, 2);
  b.ink(ink);
  // fallen platforms
  for (let i = 0; i < 2; i++) {
    const x = r.range(-span, span - 6), ang = r.jit(0.35), w = r.range(5, 7), y = r.range(1.5, 3);
    const deck: Pt[] = [[0, 0], [w, 0], [w, -0.8], [0, -0.8]].map(([px, py]) => [x + px * Math.cos(ang) - py * Math.sin(ang), y + px * Math.sin(ang) + py * Math.cos(ang)] as Pt);
    b.outlined(deck, 'shade', ARCH);
  }
  return span;
}

function domeRuins(b: InkBuilder, r: Rng): number {
  const n = r.int(1, 3), span = n * 5.5;
  const ds = Array.from({ length: n }, (_, i) => ({ x: (i - (n - 1) / 2) * 10 + r.jit(1.5), y: r.range(-2, 1), w: r.range(6, 9) }));
  ds.sort((a, c) => a.y - c.y);
  for (const d of ds) {
    const hw = d.w / 2, h = d.w * 0.35, base = d.y - h;
    b.outlined([[d.x - hw, d.y], [d.x - hw, base], [d.x + hw, base], [d.x + hw, d.y]], 'wall', ARCH);
    b.fill([[d.x + hw * 0.35, d.y], [d.x + hw * 0.35, base], [d.x + hw, base], [d.x + hw, d.y]], 'shade', false);
    // half a shell left standing, broken off in a jagged edge
    const shell = arc(d.x, base, hw * 1.02, d.w * 0.62, Math.PI, Math.PI * r.range(1.45, 1.7), 7);
    const last = shell[shell.length - 1]!;
    const brk: Pt[] = [last, [last[0] + 0.6, last[1] + d.w * 0.15], [last[0] - 0.3, last[1] + d.w * 0.3], [last[0] + 0.8, base]];
    b.outlined(shell.concat(brk), 'roof', ARCH);
    b.lines([[[d.x - hw * 0.4, base - d.w * 0.15], [d.x - hw * 0.2, base - d.w * 0.4], [d.x - hw * 0.35, base - d.w * 0.5]]], FINE, 'ink', 0.8);
  }
  rubble(b, r, span, 'roof');
  return span;
}

function toppledTower(b: InkBuilder, r: Rng): number {
  const span = 14;
  const sx = r.range(-9, -5);
  stump(b, r, sx, -0.5, r.range(5, 6), r.range(6, 9));
  // the fallen shaft lying on the ground, roof cone broken off at its end
  const L = r.range(13, 17), hh = r.range(4, 5), y = 2.8, x0 = sx + 3.5;
  b.outlined([[x0, y], [x0 + L, y - 0.6], [x0 + L, y - hh - 0.6], [x0, y - hh]], 'wall', ARCH);
  b.fill([[x0, y], [x0 + L, y - 0.6], [x0 + L, y - hh * 0.35 - 0.6], [x0, y - hh * 0.35]], 'shade', false);
  const rings: Pt[][] = [];
  for (let i = 1; i < 4; i++) { const x = x0 + (L * i) / 4; rings.push([[x, y - (0.6 * i) / 4], [x, y - hh - (0.6 * i) / 4]]); }
  b.lines(rings, FINE * 0.7, 'ink', 0.6);
  const cx = x0 + L + 1.2;
  b.outlined([[cx, y - 0.4], [cx + r.range(5, 7), y - hh * 0.5], [cx + 0.4, y - hh - 0.8]], 'roof', ARCH);
  rubble(b, r, span, 'wall');
  return span;
}
