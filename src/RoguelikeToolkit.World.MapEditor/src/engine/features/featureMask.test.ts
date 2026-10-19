import { describe, expect, it } from 'vitest';
import type { FeatureEntity } from '../doc/types';
import { featureMask, maskHas, tileRowSpans, type MaskSource } from './featureMask';

const W = 60, H = 30;
/** Tiles are 20-pixel columns (0, 1, 2); water covers x < 26, so the shore sits 6 px into tile 1. */
function source(features: FeatureEntity[]): MaskSource {
  const tile = new Int32Array(W * H), elevation = new Float32Array(W * H), biome = new Uint8Array(W * H);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const i = y * W + x;
    tile[i] = Math.floor(x / 20);
    elevation[i] = x < 26 ? -0.1 : 0.2;
    biome[i] = x < 26 ? 0 : 3;
  }
  return { width: W, height: H, elevation, biome, tile, rows: tileRowSpans(tile, W, 3), owners: (t) => features.filter((f) => f.tiles?.includes(t)) };
}
const feature = (id: string, kind: FeatureEntity['kind'], tiles: number[]): FeatureEntity =>
  ({ id, kind, name: id, x: 0, y: 0, angle: 0, extent: 0, weight: 1, tiles, details: [], culture: '' } as FeatureEntity);

describe('featureMask', () => {
  it('runs a lake out to its drawn shore, past its tile', () => {
    const lake = feature('lake', 'lake', [0]);
    const m = featureMask(source([lake]), lake)!;
    expect(maskHas(m, 3, 10)).toBe(true);
    expect(maskHas(m, 24, 10)).toBe(true);    // shore water on tile 1
    expect(maskHas(m, 27, 10)).toBe(false);   // dry land
  });

  it('stops at a sibling water feature\'s tiles', () => {
    const a = feature('a', 'lake', [0]), b = feature('b', 'sea', [1]);
    const m = featureMask(source([a, b]), a)!;
    expect(maskHas(m, 19, 10)).toBe(true);
    expect(maskHas(m, 21, 10)).toBe(false);
  });

  it('keeps a forest to the forest pixels of its tiles and their edge', () => {
    const f = feature('f', 'forest', [2]);
    const m = featureMask(source([f]), f)!;
    expect(maskHas(m, 30, 10)).toBe(true);    // forest on tile 1, reached from tile 2
    expect(maskHas(m, 50, 10)).toBe(true);
    expect(maskHas(m, 20, 10)).toBe(false);   // water
  });

  it('keeps a forest off lake water even where the biome raster still says forest', () => {
    const f = feature('f', 'forest', [1, 2]);
    const src = source([f]);
    src.biome.fill(3);   // the biome survives under water
    const m = featureMask(src, f)!;
    expect(maskHas(m, 10, 10)).toBe(false);   // water
    expect(maskHas(m, 30, 10)).toBe(true);
  });

  it('keeps a range or valley off the water and rounds its outline', () => {
    const v = feature('v', 'valley', [1]);
    const m = featureMask(source([v]), v)!;
    expect(maskHas(m, 22, 10)).toBe(false);   // water on its own tile
    expect(maskHas(m, 30, 15)).toBe(true);
    expect(maskHas(m, 55, 15)).toBe(false);
  });
});
