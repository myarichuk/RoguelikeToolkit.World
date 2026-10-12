import { describe, expect, it } from 'vitest';
import { collectEdits, editsFromRows, editsToRows, MIN_DELTA, packEdits, tilePixelCounts, type Raster, type TileEdits } from './edits';

// Four tiles of four pixels each: tile t owns pixels 4t..4t+3.
const TILES = 4, PX = 16;
const tile = Int32Array.from({ length: PX }, (_, i) => Math.floor(i / 4));
const base = (): Raster => ({ elevation: new Float32Array(PX).fill(0.3), biome: new Uint8Array(PX).fill(1) });
const clone = (r: Raster): Raster => ({ elevation: r.elevation.slice(), biome: r.biome.slice() });

describe('collectEdits', () => {
  it('reports nothing when nothing changed', () => {
    const b = base();
    const r = collectEdits(clone(b), b, tile, TILES, new Map(), 'sculpt');
    expect(r.edits.size).toBe(0);
    expect(r.heightTiles + r.biomeTiles).toBe(0);
  });

  it('averages the pixel change over the whole tile, so a thin dab is a small sculpt', () => {
    const b = base(), c = clone(b);
    c.elevation[4] += 0.4;                     // one of tile 1's four pixels
    const r = collectEdits(c, b, tile, TILES, new Map(), 'sculpt');
    expect(r.edits.get(1)!.dh).toBeCloseTo(0.1, 5);
    expect(r.edits.size).toBe(1);
  });

  it('ignores a stray dab below the threshold', () => {
    const b = base(), c = clone(b);
    c.elevation[0] += MIN_DELTA * 2;           // mean over 4 pixels = MIN_DELTA / 2
    expect(collectEdits(c, b, tile, TILES, new Map(), 'sculpt').edits.size).toBe(0);
  });

  it('accumulates sculpts on top of the prior table and releases a height pin', () => {
    const b = base(), c = clone(b);
    for (let i = 8; i < 12; i++) c.elevation[i] += 0.5;
    const prior: TileEdits = new Map([[2, { dh: 0.25, pinH: 0.9 }]]);
    const e = collectEdits(c, b, tile, TILES, prior, 'sculpt').edits.get(2)!;
    expect(e.dh).toBeCloseTo(0.75, 5);
    expect(e.pinH).toBeUndefined();
    expect(prior.get(2)!.pinH).toBe(0.9);      // the prior table is not mutated
  });

  it('fix mode pins the tile to its current mean height and leaves the sculpt delta alone', () => {
    const b = base(), c = clone(b);
    for (let i = 12; i < 16; i++) c.elevation[i] = 0.8;
    const e = collectEdits(c, b, tile, TILES, new Map([[3, { dh: 0.1 }]]), 'fix').edits.get(3)!;
    expect(e.pinH).toBeCloseTo(0.8, 5);
    expect(e.dh).toBeCloseTo(0.1, 5);
  });

  it('pins a biome when enough of the tile was repainted, and only with the dominant colour', () => {
    const b = base(), c = clone(b);
    c.biome[0] = 2; c.biome[1] = 2; c.biome[2] = 6;       // 3 of 4 pixels repainted, 2 of them desert
    const r = collectEdits(c, b, tile, TILES, new Map(), 'sculpt');
    expect(r.edits.get(0)!.pinBiome).toBe(2);
    expect(r.biomeTiles).toBe(1);
  });

  it('does not pin a barely touched tile', () => {
    const b = base(), c = clone(b);
    c.biome[4] = 2;                                        // 1 of 4 = 25% < 40%
    expect(collectEdits(c, b, tile, TILES, new Map(), 'sculpt').edits.size).toBe(0);
  });

  it('keeps editor-only biomes (savanna, farmland, ashlands) out of the physics', () => {
    const b = base(), c = clone(b);
    c.biome.fill(10, 0, 4); c.biome.fill(11, 4, 8); c.biome.fill(13, 8, 12);
    expect(collectEdits(c, b, tile, TILES, new Map(), 'sculpt').edits.size).toBe(0);
  });

  it('skips pixels with no tile (hand-made maps)', () => {
    const b = base(), c = clone(b);
    const none = new Int32Array(PX).fill(-1);
    c.elevation.fill(1);
    expect(collectEdits(c, b, none, TILES, new Map(), 'sculpt').edits.size).toBe(0);
  });
});

describe('edit table encoding', () => {
  const table: TileEdits = new Map([[7, { dh: 0.5 }], [2, { pinH: 0.9, pinBiome: 12 }], [5, { pinBiome: 2 }]]);

  it('packs in ascending tile order with Core flag bits and the biome above them', () => {
    const p = packEdits(table);
    expect([...p.tiles]).toEqual([2, 5, 7]);
    expect([...p.meta]).toEqual([1 | 2 | (12 << 8), 2 | (2 << 8), 0]);
    expect(p.pinHeight[0]).toBeCloseTo(0.9);
    expect(p.delta[2]).toBeCloseTo(0.5);
  });

  it('round-trips through the JSON rows a .wmap stores', () => {
    const back = editsFromRows(JSON.parse(JSON.stringify(editsToRows(table))));
    expect([...back.entries()].sort()).toEqual([...table.entries()].sort());
  });

  it('counts pixels per tile', () => {
    expect([...tilePixelCounts(tile, TILES)]).toEqual([4, 4, 4, 4]);
  });
});
