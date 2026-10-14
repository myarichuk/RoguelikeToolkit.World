import { describe, expect, it } from 'vitest';
import { biomeDab, edgeNoise, falloff, heightDab, strokePoints, type Dab } from './brushes';

const W = 32, H = 24;
const dab = (over: Partial<Dab> = {}): Dab => ({ x: 16, y: 12, radius: 6, strength: 1, ...over });

describe('edgeNoise', () => {
  it('is deterministic and stays in [0,1)', () => {
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const n = edgeNoise(7, x, y);
      expect(n).toBeGreaterThanOrEqual(0);
      expect(n).toBeLessThan(1);
      expect(edgeNoise(7, x, y)).toBe(n);
    }
  });

  it('varies in patches, not per-texel static: neighbours are close on average', () => {
    let sum = 0, n = 0, max = 0;
    for (let y = 0; y < H; y++) for (let x = 0; x < W - 1; x++) {
      const d = Math.abs(edgeNoise(11, x + 1, y) - edgeNoise(11, x, y));
      sum += d; n++; max = Math.max(max, d);
    }
    // Independent uniform noise would average ~1/3 per step; correlated noise stays far below.
    expect(sum / n).toBeLessThan(0.2);
    expect(max).toBeLessThan(0.75);
  });

  it('differs between seeds', () => {
    let same = 0;
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++)
      if (edgeNoise(1, x, y) === edgeNoise(2, x, y)) same++;
    expect(same).toBeLessThan(W * H);
  });
});

describe('biomeDab', () => {
  it('fills the heart solidly and stops at the rim', () => {
    const data = new Uint8Array(W * H).fill(1);
    biomeDab(data, W, H, dab(), 3, 7);
    expect(data[12 * W + 16]).toBe(3);                       // centre
    expect(data[12 * W + 15]).toBe(3);                       // solid heart, no speckle holes
    expect(data[12 * W + 16 - 7]).toBe(1);                   // beyond radius + margin
    expect(data[0]).toBe(1);
  });

  it('paints a partial, blobby rim rather than all-or-nothing static', () => {
    const data = new Uint8Array(W * H).fill(1);
    biomeDab(data, W, H, dab(), 3, 7);
    let rim = 0, rimFilled = 0, isolated = 0;
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const dx = x + 0.5 - 16, dy = y + 0.5 - 12, r = Math.hypot(dx, dy) / 6;
      if (r < 0.75 || r >= 1) continue;                      // the wobble band only
      rim++;
      if (data[y * W + x] !== 3) continue;
      rimFilled++;
      const nb = [[1, 0], [-1, 0], [0, 1], [0, -1]]
        .filter(([ox, oy]) => data[(y + oy) * W + (x + ox + W) % W] === 3);
      if (nb.length === 0) isolated++;
    }
    expect(rim).toBeGreaterThan(0);
    expect(rimFilled).toBeGreaterThan(0);
    expect(rimFilled).toBeLessThan(rim);                     // partial, not a hard disc
    expect(isolated / Math.max(1, rimFilled)).toBeLessThan(0.25); // patches, not spray
  });

  it('is deterministic and wraps around the antimeridian', () => {
    const a = new Uint8Array(W * H).fill(1), b = new Uint8Array(W * H).fill(1);
    biomeDab(a, W, H, dab({ x: 0.4 }), 3, 9);
    biomeDab(b, W, H, dab({ x: 0.4 }), 3, 9);
    expect(a).toEqual(b);
    const left = a.slice(12 * W, 12 * W + 3).some((v) => v === 3);
    const right = a.slice(12 * W + W - 3, 12 * W + W).some((v) => v === 3);
    expect(left && right).toBe(true);
  });

  it('a light touch stipples instead of flooding', () => {
    const faint = new Uint8Array(W * H).fill(1), firm = new Uint8Array(W * H).fill(1);
    biomeDab(faint, W, H, dab({ strength: 0.15 }), 3, 7);
    biomeDab(firm, W, H, dab({ strength: 1 }), 3, 7);
    const count = (d: Uint8Array) => d.reduce((s, v) => s + (v === 3 ? 1 : 0), 0);
    expect(count(faint)).toBeGreaterThan(0);
    expect(count(faint)).toBeLessThan(count(firm));
  });
});

describe('heightDab roughen', () => {
  it('stays within strength and makes lumps, not spikes', () => {
    const data = new Float32Array(W * H).fill(0.4);
    heightDab(data, W, H, dab({ strength: 0.1 }), 'roughen', 0, 5);
    let maxStep = 0;
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const v = data[y * W + x] - 0.4;
      expect(Math.abs(v)).toBeLessThanOrEqual(0.1);
      if (x + 1 < W) maxStep = Math.max(maxStep, Math.abs(data[y * W + x + 1] - data[y * W + x]));
    }
    // Correlated lumps change gradually; white noise would regularly jump the full range.
    expect(maxStep).toBeLessThan(0.1);
  });
});

describe('falloff and strokePoints', () => {
  it('falloff is 1 at the centre and C1 at the rim', () => {
    expect(falloff(0)).toBe(1);
    expect(falloff(1)).toBe(0);
    expect(falloff(0.999)).toBeCloseTo(0, 4);
  });

  it('strokePoints spaces dabs evenly and carries the remainder', () => {
    const { pts, carry } = strokePoints(0, 0, 10, 0, 3, 0);
    expect(pts.map(([x]) => x)).toEqual([3, 6, 9]);
    expect(carry).toBeCloseTo(1, 10);
  });
});
