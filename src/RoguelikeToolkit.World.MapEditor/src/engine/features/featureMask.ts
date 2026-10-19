import type { FeatureEntity } from '../doc/types';
import type { FeatureKind } from '../world/protocol';
import { boxBlur, valueNoise } from '../world/rasterize';

/** A feature's footprint as drawn, one byte per map pixel (1 inside): rows y0..y0+h-1 at full map width (x wraps). */
export interface FeatureMask { y0: number; h: number; width: number; data: Uint8Array; }

/** What a mask is computed from: the map rasters and which features own each tile. */
export interface MaskSource {
  width: number;
  height: number;
  elevation: Float32Array;
  biome: Uint8Array;
  /** Dominant tile per pixel. */
  tile: Int32Array;
  /** Area features owning a tile. */
  owners(tile: number): readonly FeatureEntity[];
  /** Per tile, the first and last raster row it is dominant on (-1, -1 where none): see `tileRowSpans`. */
  rows: Int32Array;
}

/** First and last row of each tile in a dominant-tile raster, interleaved; -1 where a tile owns no pixel. */
export function tileRowSpans(tile: Int32Array, width: number, nTiles: number): Int32Array {
  const rows = new Int32Array(nTiles * 2).fill(-1);
  for (let i = 0; i < tile.length; i++) {
    const t = tile[i];
    if (t < 0 || t >= nTiles) continue;
    const y = (i / width) | 0;
    if (rows[t * 2] < 0) rows[t * 2] = y;
    rows[t * 2 + 1] = y;
  }
  return rows;
}

/** Features that share a drawn medium split it between them; a mask never grows into a sibling's tiles. */
const GROUP: Partial<Record<FeatureKind, 'water' | 'forest' | 'relief'>> = {
  lake: 'water', sea: 'water', ocean: 'water', forest: 'forest', mountain: 'relief', valley: 'relief',
};
const FOREST_BIOMES = new Set([3, 6]);
/** How far (pixels) a water or forest mask may run past its tiles to reach the drawn shore or forest edge. */
const GROW_PX = 24;
/** Box blur radius (pixels, applied twice) that rounds the hex outline of a feature without a drawn edge. */
const BLUR_PX = 4;

export const maskHas = (m: FeatureMask, x: number, y: number): boolean => {
  const yi = Math.floor(y) - m.y0;
  if (yi < 0 || yi >= m.h) return false;
  const xi = ((Math.floor(x) % m.width) + m.width) % m.width;
  return m.data[yi * m.width + xi] === 1;
};

/**
 * The pixels a feature covers on the drawn map. Core hands over features as icosphere tiles, whose
 * union is a hex staircase that ignores the drawn coast, lake shore and forest edge. Water and
 * forest features take the drawn pixels instead: water (or forest) on their tiles, flooded out
 * through more of it up to `GROW_PX` (the shore pixels whose dominant tile is a land tile) but never
 * into a sibling's tiles. Features with nothing drawn to follow (ranges, valleys) round their tile
 * outline with a blur and a noisy threshold, and keep to land.
 */
export function featureMask(src: MaskSource, f: FeatureEntity): FeatureMask | null {
  if (!f.tiles?.length || f.kind === 'river') return null;
  const W = src.width, H = src.height, tile = src.tile;
  const nT = src.rows.length / 2;
  const mine = new Uint8Array(nT);
  let ymin = H, ymax = -1;
  for (const t of f.tiles) {
    if (t < 0 || t >= nT || src.rows[t * 2] < 0) continue;
    mine[t] = 1;
    ymin = Math.min(ymin, src.rows[t * 2]); ymax = Math.max(ymax, src.rows[t * 2 + 1]);
  }
  if (ymax < 0) return null;
  const own = { has: (t: number) => t >= 0 && mine[t] === 1 };
  const y0 = Math.max(0, ymin - GROW_PX), h = Math.min(H - 1, ymax + GROW_PX) - y0 + 1;
  const data = new Uint8Array(h * W);
  const group = GROUP[f.kind];
  const at = (i: number) => y0 * W + i; // mask index -> raster index

  if (group === 'water' || group === 'forest') {
    const pred = group === 'water'
      ? (r: number) => src.elevation[r] < 0
      : (r: number) => FOREST_BIOMES.has(src.biome[r]) && src.elevation[r] >= 0;   // the biome raster keeps its value under lakes
    const sib = new Int8Array(nT).fill(-1); // per tile: owned by a sibling of the same group (lazy)
    const sibling = (r: number) => {
      const t = tile[r];
      if (t < 0 || t >= nT || mine[t]) return false;
      if (sib[t] < 0) sib[t] = src.owners(t).some((o) => o.id !== f.id && GROUP[o.kind] === group) ? 1 : 0;
      return sib[t] === 1;
    };
    const visit = (j: number, d: number) => {
      if (dist[j] !== 255) return;
      const r = at(j);
      if (!pred(r) || sibling(r)) return;
      dist[j] = d; data[j] = 1; queue[tail++] = j;
    };
    const dist = new Uint8Array(h * W).fill(255);
    const queue = new Int32Array(h * W);
    let head = 0, tail = 0;
    for (let i = 0; i < h * W; i++) {
      const r = at(i);
      if (own.has(tile[r]) && pred(r)) { data[i] = 1; dist[i] = 0; queue[tail++] = i; }
    }
    while (head < tail) {
      const i = queue[head++], d = dist[i] + 1;
      if (d > GROW_PX) continue;
      const x = i % W, row = i - x;
      visit(row + (x + 1) % W, d);
      visit(row + (x + W - 1) % W, d);
      if (row > 0) visit(i - W, d);
      if (row < (h - 1) * W) visit(i + W, d);
    }
    return { y0, h, width: W, data };
  }

  const a = new Float32Array(h * W), b = new Float32Array(h * W);
  for (let i = 0; i < h * W; i++) a[i] = own.has(tile[at(i)]) ? 1 : 0;
  for (let pass = 0; pass < 2; pass++) {
    boxBlur(a, b, W, h, BLUR_PX, true);
    boxBlur(b, a, W, h, BLUR_PX, false);
  }
  for (let i = 0; i < h * W; i++) {
    const r = at(i), x = i % W, y = y0 + (i - x) / W;
    const thr = 0.5 + 0.3 * (valueNoise(x / 6, y / 6, 0x3a5c) - 0.5);
    data[i] = a[i] > thr && src.elevation[r] >= 0 ? 1 : 0;
  }
  return { y0, h, width: W, data };
}
