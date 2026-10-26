import { describe, expect, it } from 'vitest';
import { MAP_W, vecToMap } from '../../core/geo';
import { buildRoads, chaikin, ROAD_STRIDE } from './roads';

/** Tiles along the equator, 10 degrees apart: tile i sits at longitude (i * 10 + lon0). */
const ring = (n: number, lon0 = 0) => {
  const v = new Float64Array(n * 3);
  for (let i = 0; i < n; i++) { const a = (lon0 + i * 10) * Math.PI / 180; v[i * 3] = Math.cos(a); v[i * 3 + 1] = Math.sin(a); }
  return v;
};
const seg = (a: number, b: number, keeper = -1, ra = 0, rb = 5, severed = -1) => [a, b, keeper, ra, rb, 3, severed];
const dry = () => false;

describe('buildRoads', () => {
  it('chains consecutive segments of one route into one smooth road', () => {
    const roads = buildRoads({ segments: [...seg(0, 1), ...seg(1, 2), ...seg(2, 3)], vectors: ring(6), wet: dry, seed: 1 });
    expect(roads).toHaveLength(1);
    expect(roads[0].kind).toBe('trunk');
    expect(roads[0].points.length / 3).toBeGreaterThan(4);   // corner cutting adds points
    expect(roads[0].points[2]).toBeGreaterThan(0);           // carries a width
  });

  it('draws the same road from tile positions as from tile vectors', () => {
    const segments = [...seg(0, 1), ...seg(1, 2), ...seg(2, 3)], v = ring(6);
    const [byVector] = buildRoads({ segments, vectors: v, wet: dry, seed: 1 });
    const [byXY] = buildRoads({ segments, xy: (t) => { const q = vecToMap(v[t * 3], v[t * 3 + 1], v[t * 3 + 2]); return [q.x, q.y]; }, wet: dry, seed: 1 });
    expect([...byXY.points]).toEqual([...byVector.points]);
  });

  it('keeps the ends on their settlements', () => {
    const [r] = buildRoads({ segments: [...seg(0, 1), ...seg(1, 2)], vectors: ring(4), wet: dry, seed: 1 });
    const first = r.points[0], last = r.points[r.points.length - 3];
    expect(first).toBeCloseTo(MAP_W / 2, 3);          // lon 0 is the map's middle
    expect(last).toBeGreaterThan(first);
  });

  it('tells a trunk from a spur by its keeper and carries severed turns', () => {
    const roads = buildRoads({ segments: [...seg(0, 1, -1, 0, 3), ...seg(2, 3, 2, 2, 3, 7)], vectors: ring(5), wet: dry, seed: 1 });
    expect(roads.map((r) => [r.kind, r.keeper, r.severed])).toEqual([['trunk', -1, -1], ['spur', 2, 7]]);
  });

  it('records when a road was founded', () => {
    const [r] = buildRoads({ segments: [0, 1, -1, 0, 5, 4, -1, 1, 2, -1, 0, 5, 2, -1], vectors: ring(4), wet: dry, seed: 1 });
    expect(r.founded).toBe(2);
  });

  it('starts a new piece where the chain breaks', () => {
    const roads = buildRoads({ segments: [...seg(0, 1), ...seg(3, 4)], vectors: ring(6), wet: dry, seed: 1 });
    expect(roads).toHaveLength(2);
  });

  it('breaks a road at open water and bridges a short wet patch', () => {
    const vectors = ring(8);
    const lon = (x: number) => (x / MAP_W) * 360 - 180;
    const wetBand = (lo: number, hi: number) => (x: number) => lon(x) >= lo && lon(x) <= hi;
    const segs = [...seg(0, 1), ...seg(1, 2), ...seg(2, 3), ...seg(3, 4), ...seg(4, 5), ...seg(5, 6)];
    const broken = buildRoads({ segments: segs, vectors, wet: wetBand(14, 36), seed: 1 });
    expect(broken.length).toBe(2);
    const bridged = buildRoads({ segments: segs, vectors, wet: wetBand(14, 16), seed: 1 });
    expect(bridged.length).toBe(1);
  });

  it('never jumps across the antimeridian', () => {
    const [r] = buildRoads({ segments: [...seg(0, 1), ...seg(1, 2)], vectors: ring(4, 170), wet: dry, seed: 1 });
    for (let i = 3; i < r.points.length; i += 3) expect(Math.abs(r.points[i] - r.points[i - 3])).toBeLessThan(MAP_W / 4);
  });

  it('is deterministic', () => {
    const o = { segments: [...seg(0, 1), ...seg(1, 2)], vectors: ring(4), wet: dry, seed: 9 };
    expect(buildRoads(o)).toEqual(buildRoads(o));
  });

  it('draws nothing for no history', () => {
    expect(buildRoads({ segments: [], vectors: ring(2), wet: dry, seed: 1 })).toEqual([]);
    expect(ROAD_STRIDE).toBe(7);
  });
});

describe('chaikin', () => {
  it('keeps both ends and rounds the corners between', () => {
    const out = chaikin([0, 0, 10, 0, 10, 10]);
    expect(out.slice(0, 2)).toEqual([0, 0]);
    expect(out.slice(-2)).toEqual([10, 10]);
    expect(out.length).toBeGreaterThan(6);
  });
});
