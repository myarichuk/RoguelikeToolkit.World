import { describe, expect, it } from 'vitest';
import { clipToLand, extractFeatures, meander, taperHead, type CatalogInput } from './features';
import { polylineDistance } from '../tools/pick';

/** Four land tiles around the equator with one 3-tile sink river running through them. */
function riverWorld(): CatalogInput {
  const lonLat: [number, number][] = [[-0.1, 0.3], [0.1, 0.3], [-0.1, -0.3], [0.1, -0.3]];
  const vectors = new Float64Array(12);
  lonLat.forEach(([lo, la], i) => {
    vectors[i * 3] = Math.cos(la) * Math.cos(lo);
    vectors[i * 3 + 1] = Math.cos(la) * Math.sin(lo);
    vectors[i * 3 + 2] = Math.sin(la);
  });
  return {
    seed: 7,
    vectors,
    faces: Int32Array.from([0, 1, 2, 1, 3, 2]),
    heights: Float64Array.from([0.2, 0.2, 0.2, 0.2]),
    biomes: Uint8Array.from([1, 1, 1, 1]),
    flow: Float64Array.from([10, 100, 10, 0]),
    climate: Float64Array.from([0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]),
    tilePlate: Uint32Array.from([0, 0, 0, 0]),
    rivers: Int32Array.from([1, 0, 2, -1, 3, 0, 1, 2]),
    waterBodies: Int32Array.from([0]),
    ranges: Int32Array.from([0]),
    deposits: Int32Array.from([0]),
    citySites: Int32Array.from([]),
    citySiteReasons: [],
    styles: [{ race: 'human', culture: 'fantasy_common', label: 'Common' }],
  };
}

/** Eight equatorial tiles: 0-6 land hills, 7 sea; a short tributary, a coastal river and a long tributary. */
function tributaryWorld(): CatalogInput {
  const lonLat: [number, number][] = [[-0.35, 0], [-0.25, 0.04], [-0.15, -0.04], [-0.05, 0], [0.05, 0.03], [0.15, -0.03], [0.25, 0], [0.35, 0]];
  const vectors = new Float64Array(24);
  lonLat.forEach(([lo, la], i) => {
    vectors[i * 3] = Math.cos(la) * Math.cos(lo);
    vectors[i * 3 + 1] = Math.cos(la) * Math.sin(lo);
    vectors[i * 3 + 2] = Math.sin(la);
  });
  return {
    seed: 7,
    vectors,
    faces: Int32Array.from([0, 1, 2, 1, 2, 3, 2, 3, 4, 3, 4, 5, 4, 5, 6, 5, 6, 7]),
    heights: Float64Array.from([0.2, 0.25, 0.3, 0.22, 0.28, 0.24, 0.2, -0.5]),
    biomes: Uint8Array.from([1, 1, 1, 1, 1, 1, 1, 1]),
    flow: Float64Array.from([10, 20, 30, 40, 50, 60, 70, 80]),
    climate: Float64Array.from([0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]),
    tilePlate: Uint32Array.from([0, 0, 0, 0, 0, 0, 0, 0]),
    rivers: Int32Array.from([3, 0, 0, -1, 3, 0, 1, 2, 1, 0, -1, 3, 4, 5, 7, 2, 0, -1, 6, 0, 1, 2, 3, 4, 5]),
    waterBodies: Int32Array.from([0]),
    ranges: Int32Array.from([0]),
    deposits: Int32Array.from([0]),
    citySites: Int32Array.from([]),
    citySiteReasons: [],
    styles: [{ race: 'human', culture: 'fantasy_common', label: 'Common' }],
  };
}

describe('river geometry', () => {
  it('draws channels from a pen line at the source to a broad river downstream', () => {
    const set = extractFeatures(riverWorld());
    expect(set.features.filter((f) => f.kind === 'river').map((f) => f.key)).toEqual(['river:0']);
    expect(set.rivers).toHaveLength(1);
    let max = 0;
    const pts = set.rivers[0].points;
    for (let i = 2; i < pts.length; i += 3) max = Math.max(max, pts[i]);
    expect(max).toBeGreaterThanOrEqual(2);      // 10x the smallest discharge
    expect(max).toBeLessThanOrEqual(5);
    expect(pts[2]).toBeLessThan(0.5);            // the source tapers to a hairline
  });

  it('draws short tributaries but names only rivers reaching water, sinks or long confluences', () => {
    const set = extractFeatures(tributaryWorld());
    // All three reaches draw ribbons, including the short headwater trickle.
    expect(set.rivers).toHaveLength(3);
    const keys = set.features.filter((f) => f.kind === 'river').map((f) => f.key);
    // The 3-tile trickle ending at a confluence in the hills earns no name.
    expect(keys).toEqual(['river:1', 'river:2']);
    const flowsInto = (key: string) => {
      const f = set.features.find((g) => g.key === key)!;
      return f.details.find(([k]) => k === 'Flows into')![1];
    };
    expect(flowsInto('river:1')).toBe('the sea');
    // A named confluence reach says so instead of claiming the sea it never touches.
    expect(flowsInto('river:2')).toBe('another river');
  });

  it('ends a tributary on its parent\'s drawn line, not a tile short of it', () => {
    const set = extractFeatures(tributaryWorld());
    // Reach 0 stops on land (tile 2): Core trims it one tile before the reach it joins.
    const trib = set.rivers[0].points, n = trib.length / 3;
    const ex = trib[(n - 1) * 3], ey = trib[(n - 1) * 3 + 1];
    const gap = Math.min(...set.rivers.slice(1).map((r) => polylineDistance(r.points, 3, ex, ey)));
    expect(gap).toBeLessThan(0.01);
  });

  it('reads a river ending in a lake as a mouth, not a confluence', () => {
    const c = tributaryWorld();
    // Tile 6 becomes a lake (ponded land, height >= 0) and the coastal river ends in it.
    c.waterBodies = Int32Array.from([1, 9, 0, 1, 6]);
    c.rivers = Int32Array.from([1, 4, 1, 9, 4, 3, 4, 5, 6]);
    const set = extractFeatures(c);
    const f = set.features.find((g) => g.key === 'river:4')!;
    expect(f.details.find(([k]) => k === 'Flows into')![1]).toBe('a lake');
  });

  it('clips a river to the drawn shore at its mouth and at a lake outflow', () => {
    const line = new Float32Array([0, 0, 2, 10, 0, 2, 20, 0, 2, 30, 0, 2, 40, 0, 2, 50, 0, 2]);
    const wet = (x: number) => x < 12 || x > 33;
    const out = clipToLand(line, wet, true);
    expect(Array.from(out.filter((_, i) => i % 3 === 0))).toEqual([10, 20, 30, 40]);
    // A tributary keeps its end (it meets a river, not a shore).
    expect(clipToLand(line, wet, false).at(-3)).toBe(50);
  });

  it('tapers the source to a hairline and leaves the channel alone', () => {
    const p = taperHead(meander([0, 0, 3, 20, 0, 3, 40, 0, 3], () => 0.5));
    expect(p[2]).toBeLessThan(1);
    expect(p[p.length - 1]).toBe(3);
  });
});
