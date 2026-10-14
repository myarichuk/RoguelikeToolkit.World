import { describe, expect, it } from 'vitest';
import { extractFeatures, type CatalogInput } from './features';

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
  it('draws channels wide enough to survive the sub-pixel fade', () => {
    const set = extractFeatures(riverWorld());
    expect(set.features.filter((f) => f.kind === 'river').map((f) => f.key)).toEqual(['river:0']);
    expect(set.rivers).toHaveLength(1);
    let max = 0;
    const pts = set.rivers[0].points;
    for (let i = 2; i < pts.length; i += 3) max = Math.max(max, pts[i]);
    expect(max).toBeGreaterThanOrEqual(2.5);
    expect(max).toBeLessThanOrEqual(6);
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
});
