// Map decoration (compass rose).
import { InkBuilder, Rng, taper, penProfile, ellipse } from '../ink';
import type { Pt, PaletteToken, Stamp } from '../ink';
import { Params } from './common';

// ================================================================ decoration

export function genCompass(r: Rng, p: Params): Stamp {
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
