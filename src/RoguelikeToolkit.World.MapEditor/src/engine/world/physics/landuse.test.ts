import { describe, expect, it } from 'vitest';
import { LAND_USES, composeBiomes, migrateLegacyLandUse } from './landuse';
import { LANDUSE_HINTS } from '../../../ui/glossary';

const W = 8;

describe('composeBiomes', () => {
  it('draws the land use slot on land, never on sea, and leaves the biome raster alone', () => {
    const biome = Uint8Array.from([0, 1, 3, 3, 1, 1, 1, 1]);
    const use = Uint8Array.from([1, 1, 2, 0, 0, 0, 0, 0]);
    const out = new Uint8Array(W);
    composeBiomes(biome, use, out, W);
    expect([...out]).toEqual([0, 11, 13, 3, 1, 1, 1, 1]);
    expect([...biome]).toEqual([0, 1, 3, 3, 1, 1, 1, 1]);
  });
  it('only rewrites the rows in the rect', () => {
    const biome = new Uint8Array(W * 4).fill(3), use = new Uint8Array(W * 4).fill(1), out = new Uint8Array(W * 4).fill(99);
    composeBiomes(biome, use, out, W, { x0: 0, y0: 1, x1: 4, y1: 2 });
    expect([...out.subarray(0, W)]).toEqual(new Array(W).fill(99));
    expect([...out.subarray(W, 2 * W)]).toEqual(new Array(W).fill(11));
    expect([...out.subarray(2 * W)]).toEqual(new Array(2 * W).fill(99));
  });
});

describe('migrateLegacyLandUse', () => {
  it('is a no-op on a clean map', () => {
    const biome = new Uint8Array(W * 2).fill(3), use = new Uint8Array(W * 2);
    expect(migrateLegacyLandUse(biome, use, W)).toBe(0);
    expect(biome.every((b) => b === 3) && use.every((u) => u === 0)).toBe(true);
  });
  it('gives each legacy pixel the nearest ordinary biome, wrapping across the antimeridian', () => {
    // Row of 8: forest(3) at x=0..1, farmland run 2..5, desert(2) at 6..7. Ties are fed from the nearer source.
    const biome = Uint8Array.from([3, 3, 11, 11, 11, 11, 2, 2]);
    const use = new Uint8Array(W);
    expect(migrateLegacyLandUse(biome, use, W)).toBe(4);
    expect([...use]).toEqual([0, 0, 1, 1, 1, 1, 0, 0]);
    expect([...biome]).toEqual([3, 3, 3, 3, 2, 2, 2, 2]);
    // x wraps: 7 is next to 0, so a run touching the seam still finds a neighbour.
    const b2 = Uint8Array.from([11, 11, 1, 1, 1, 1, 1, 5]);
    migrateLegacyLandUse(b2, new Uint8Array(W), W);
    expect(b2[0]).toBe(5);
  });
  it('keeps an existing land use, treats savanna as an ordinary biome, and survives an all-legacy map', () => {
    const biome = Uint8Array.from([10, 11, 11, 13]);
    const use = Uint8Array.from([0, 2, 0, 0]);
    migrateLegacyLandUse(biome, use, 4);
    expect([...use]).toEqual([0, 2, 1, 2]);
    expect(biome[1]).toBe(10);
    const all = new Uint8Array(4).fill(11), u = new Uint8Array(4);
    migrateLegacyLandUse(all, u, 4);
    expect([...all]).toEqual([1, 1, 1, 1]);
  });
});

describe('land-use tooltips', () => {
  it('give every use its own sentence, and the panel note does not claim Apply physics ignores land use', () => {
    const hints = LAND_USES.map((u) => u.hint);
    expect(new Set(hints).size).toBe(hints.length);
    for (const h of hints) expect(h.length).toBeGreaterThan(20);
    expect(LANDUSE_HINTS.note).not.toMatch(/Apply physics ignore/i);
    // The tool tip names every paintable use, so it cannot go stale when one is added.
    for (const u of LAND_USES) expect(LANDUSE_HINTS.tool.toLowerCase()).toContain(u.name.toLowerCase().replace('-up', ''));
  });
});
