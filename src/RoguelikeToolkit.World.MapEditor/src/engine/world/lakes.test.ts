import { describe, expect, it } from 'vitest';
import { extractFeatures, type CatalogInput } from './features';

/** Four tiles around the equator: xy spread out, all comfortably inside the map. */
function tinyWorld(): Omit<CatalogInput, 'waterPixels'> {
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
    flow: Float64Array.from([0, 0, 0, 0]),
    climate: Float64Array.from([0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]),
    tilePlate: Uint32Array.from([0, 0, 0, 0]),
    rivers: Int32Array.from([0]),
    // One lake body of two land tiles: ponded water the map cannot show (beds above sea level).
    waterBodies: Int32Array.from([1, 7, 0, 2, 0, 1]),
    ranges: Int32Array.from([0]),
    deposits: Int32Array.from([0]),
    citySites: Int32Array.from([]),
    citySiteReasons: [],
    styles: [{ race: 'human', culture: 'fantasy_common', label: 'Common' }],
  };
}

const lakeKeys = (c: CatalogInput): string[] =>
  extractFeatures(c).features.filter((f) => f.kind === 'lake').map((f) => f.key);

describe('phantom lakes', () => {
  it('keeps a lake when no raster is given (callers without water pixels)', () => {
    expect(lakeKeys(tinyWorld())).toEqual(['lake:7']);
  });

  it('drops a lake whose tiles cover no water pixel', () => {
    expect(lakeKeys({ ...tinyWorld(), waterPixels: Int32Array.from([0, 0, 0, 0]) })).toEqual([]);
  });

  it('keeps a lake with even one water pixel on its tiles', () => {
    const keys = lakeKeys({ ...tinyWorld(), waterPixels: Int32Array.from([0, 3, 0, 0]) });
    expect(keys).toEqual(['lake:7']);
  });

  it('never settles on drawn lake tiles, but keeps shore sites', () => {
    const c: CatalogInput = {
      ...tinyWorld(),
      waterPixels: Int32Array.from([5, 5, 0, 0]),
      citySites: Int32Array.from([0, 9000, 1, 8000, 2, 7000]),
      citySiteReasons: ['water', 'water', 'water'],
    };
    expect(lakeKeys(c)).toEqual(['lake:7']);
    const settled = extractFeatures(c).features
      .filter((f) => f.key.startsWith('settlement:')).map((f) => f.key);
    expect(settled).not.toContain('settlement:0');
    expect(settled).not.toContain('settlement:1');
    expect(settled).toContain('settlement:2');
  });
});
