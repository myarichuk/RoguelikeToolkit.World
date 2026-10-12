// Hand edits, in the tile space the physics lives in. The editor paints pixels; the world is
// computed per icosphere tile. This module is the bridge: it folds the pixel difference between
// what the user has now and what the engine last produced into a sparse per-tile table, and packs
// that table for the worker. The table (never the derived rasters) is what a document persists.

/**
 * Biome ids the editor paints that Core has no biome for (savanna): decoration only. Farmland and
 * ashlands used to be here; they are land use now (world/landuse.ts), a separate raster.
 */
export const EDITOR_ONLY_BIOMES: ReadonlySet<number> = new Set([10]);

/** Mean height change below this (height units; sea level 0, relief about ±1.2) is a stray dab, not an edit. */
export const MIN_DELTA = 0.02;
/** A tile is repainted when at least this share of its pixels now carry one other biome. */
export const MIN_BIOME_SHARE = 0.4;

export interface TileEdit {
  /** Sculpt: added to the tectonic relief before climate and erosion (erosion still acts on it). */
  dh?: number;
  /** Fix: the final height after erosion. */
  pinH?: number;
  /** Fix: the Core biome id the tile keeps. */
  pinBiome?: number;
  /** Climate brush: added to the tile's temperature (Core units, about 55 K per unit) after every physical term. */
  dt?: number;
  /** Climate brush: added to the tile's precipitation (0..1 units) after every physical term. */
  dr?: number;
  /** Land use the author painted on the tile (Core's `LandUseId`): pins the decision plugins would otherwise make. */
  lu?: number;
}

/** Full-scale climate paint: a texel at +/-127 means this much (temperature units / precipitation units). */
export const CLIMATE_RANGE = { temperature: 0.4, precipitation: 0.4 } as const;
export const PAINT_MAX = 127;
/** A tile whose mean climate paint is below this (in Core units) is a stray dab, not an edit. */
export const MIN_CLIMATE = 0.01;

export type TileEdits = Map<number, TileEdit>;
export type EditMode = 'sculpt' | 'fix';

export interface Raster { elevation: Float32Array; biome: Uint8Array; }

export interface CollectResult {
  edits: TileEdits;
  /** Tiles whose height changed by at least MIN_DELTA. */
  heightTiles: number;
  /** Tiles newly pinned to a painted biome. */
  biomeTiles: number;
}

/** Pixels per tile (the dominant-tile raster is fixed for a world, so callers may cache this). */
export function tilePixelCounts(tile: Int32Array, tileCount: number): Int32Array {
  const counts = new Int32Array(tileCount);
  for (let i = 0; i < tile.length; i++) { const t = tile[i]; if (t >= 0 && t < tileCount) counts[t]++; }
  return counts;
}

/**
 * Folds `current - baseline` into `prior`, returning a new table. `baseline` is the raster the
 * engine last produced for `prior`; anything the user has done since shows up as a difference.
 * Sculpt accumulates onto the tile's delta and releases a height pin; fix pins the tile to its
 * current mean height. Biome repaints become pins when they cover enough of the tile.
 */
export function collectEdits(
  current: Raster, baseline: Raster, tile: Int32Array, tileCount: number, prior: TileEdits, mode: EditMode,
  counts: Int32Array = tilePixelCounts(tile, tileCount),
): CollectResult {
  const sumDiff = new Float64Array(tileCount), sumCur = new Float64Array(tileCount);
  const painted = new Map<number, Map<number, number>>();
  for (let i = 0; i < tile.length; i++) {
    const t = tile[i];
    if (t < 0 || t >= tileCount) continue;
    const e = current.elevation[i];
    sumDiff[t] += e - baseline.elevation[i];
    sumCur[t] += e;
    const b = current.biome[i];
    if (b !== baseline.biome[i] && !EDITOR_ONLY_BIOMES.has(b)) {
      let m = painted.get(t);
      if (!m) painted.set(t, (m = new Map()));
      m.set(b, (m.get(b) ?? 0) + 1);
    }
  }
  const edits: TileEdits = new Map();
  for (const [t, e] of prior) edits.set(t, { ...e });
  let heightTiles = 0, biomeTiles = 0;
  for (let t = 0; t < tileCount; t++) {
    const n = counts[t];
    if (!n) continue;
    const mean = sumDiff[t] / n;
    if (Math.abs(mean) >= MIN_DELTA) {
      heightTiles++;
      const e = edits.get(t) ?? {};
      if (mode === 'sculpt') { e.dh = (e.dh ?? 0) + mean; delete e.pinH; }
      else e.pinH = sumCur[t] / n;
      edits.set(t, e);
    }
  }
  for (const [t, m] of painted) {
    let best = -1, bestN = 0;
    for (const [b, c] of m) if (c > bestN || (c === bestN && b < best)) { best = b; bestN = c; }
    if (bestN >= MIN_BIOME_SHARE * counts[t]) {
      const e = edits.get(t) ?? {};
      e.pinBiome = best;
      edits.set(t, e);
      biomeTiles++;
    }
  }
  return { edits, heightTiles, biomeTiles };
}

/**
 * Folds the two climate paint rasters into the table: a tile's offset is the mean paint over its pixels.
 * The rasters are the authority (they are what the author sees and keeps editing), so every tile's
 * `dt`/`dr` is rebuilt from them and tiles left with nothing are dropped. Returns a new table.
 */
export function collectClimate(
  warmth: Int8Array, wetness: Int8Array, tile: Int32Array, tileCount: number, prior: TileEdits,
  counts: Int32Array = tilePixelCounts(tile, tileCount),
): { edits: TileEdits; climateTiles: number } {
  const sumT = new Float64Array(tileCount), sumR = new Float64Array(tileCount);
  for (let i = 0; i < tile.length; i++) {
    const t = tile[i];
    if (t < 0 || t >= tileCount) continue;
    sumT[t] += warmth[i]; sumR[t] += wetness[i];
  }
  const edits: TileEdits = new Map();
  for (const [t, e] of prior) { const { dt: _dt, dr: _dr, ...rest } = e; if (Object.keys(rest).length) edits.set(t, rest); }
  let climateTiles = 0;
  for (let t = 0; t < tileCount; t++) {
    const n = counts[t];
    if (!n) continue;
    const dt = (sumT[t] / n / PAINT_MAX) * CLIMATE_RANGE.temperature, dr = (sumR[t] / n / PAINT_MAX) * CLIMATE_RANGE.precipitation;
    const hasT = Math.abs(dt) >= MIN_CLIMATE, hasR = Math.abs(dr) >= MIN_CLIMATE;
    if (!hasT && !hasR) continue;
    const e = edits.get(t) ?? {};
    if (hasT) e.dt = dt;
    if (hasR) e.dr = dr;
    edits.set(t, e);
    climateTiles++;
  }
  return { edits, climateTiles };
}

/** Share of a tile's pixels one land use must cover for the tile to be pinned to it. */
export const MIN_LANDUSE_SHARE = 0.4;

/**
 * Folds the land-use raster into the table as per-tile pins. Like the climate paint, the raster is the
 * authority, so every tile's `lu` is rebuilt from it: erasing the paint drops the pin. Only land pixels
 * count (a stray stroke over water is stored but never pinned), and a tile needs `MIN_LANDUSE_SHARE` of
 * its pixels in one use. Returns a new table.
 */
export function collectLandUse(
  landuse: Uint8Array, biome: Uint8Array, tile: Int32Array, tileCount: number, prior: TileEdits,
  counts: Int32Array = tilePixelCounts(tile, tileCount),
): { edits: TileEdits; landUseTiles: number } {
  const votes = new Map<number, Map<number, number>>();
  for (let i = 0; i < tile.length; i++) {
    const u = landuse[i], t = tile[i];
    if (u === 0 || t < 0 || t >= tileCount || biome[i] === 0) continue;
    let m = votes.get(t);
    if (!m) votes.set(t, (m = new Map()));
    m.set(u, (m.get(u) ?? 0) + 1);
  }
  const edits: TileEdits = new Map();
  for (const [t, e] of prior) { const { lu: _lu, ...rest } = e; if (Object.keys(rest).length) edits.set(t, rest); }
  let landUseTiles = 0;
  for (const [t, m] of votes) {
    let best = -1, bestN = 0;
    for (const [u, c] of m) if (c > bestN || (c === bestN && u < best)) { best = u; bestN = c; }
    if (bestN < MIN_LANDUSE_SHARE * counts[t]) continue;
    const e = edits.get(t) ?? {};
    e.lu = best;
    edits.set(t, e);
    landUseTiles++;
  }
  return { edits, landUseTiles };
}

export interface PackedEdits {
  tiles: Int32Array; delta: Float64Array; pinHeight: Float64Array; meta: Int32Array;
  /** Per tile climate offsets (Core's UserEditInfo.TemperatureDelta / PrecipitationDelta). */
  temperature: Float64Array; precipitation: Float64Array;
}

/** Core's UserEditFlags. */
const PIN_HEIGHT = 1, PIN_BIOME = 2, PIN_LAND_USE = 4;

/** The wire form for `WorldExports.ApplyEdits`; tile order is ascending so the table is canonical. */
export function packEdits(edits: TileEdits): PackedEdits {
  const keys = [...edits.keys()].sort((a, b) => a - b);
  const out: PackedEdits = {
    tiles: new Int32Array(keys.length), delta: new Float64Array(keys.length),
    pinHeight: new Float64Array(keys.length), meta: new Int32Array(keys.length),
    temperature: new Float64Array(keys.length), precipitation: new Float64Array(keys.length),
  };
  keys.forEach((t, k) => {
    const e = edits.get(t)!;
    let flags = 0;
    if (e.pinH !== undefined) flags |= PIN_HEIGHT;
    if (e.pinBiome !== undefined) flags |= PIN_BIOME;
    if (e.lu !== undefined) flags |= PIN_LAND_USE;
    out.tiles[k] = t;
    out.delta[k] = e.dh ?? 0;
    out.pinHeight[k] = e.pinH ?? 0;
    out.meta[k] = flags | ((e.pinBiome ?? 0) << 8) | ((e.lu ?? 0) << 16);
    out.temperature[k] = e.dt ?? 0;
    out.precipitation[k] = e.dr ?? 0;
  });
  return out;
}

/** Plain JSON for the .wmap header: `[tile, dh, pinH, pinBiome, dt, dr, lu]` rows, null for "unset". Older files have fewer columns (four before the climate brush, six before land-use pins). */
export type EditRow = [number, number | null, number | null, number | null, (number | null)?, (number | null)?, (number | null)?];

export function editsToRows(edits: TileEdits): EditRow[] {
  return [...edits.keys()].sort((a, b) => a - b).map((t) => {
    const e = edits.get(t)!;
    return [t, e.dh ?? null, e.pinH ?? null, e.pinBiome ?? null, e.dt ?? null, e.dr ?? null, e.lu ?? null];
  });
}

export function editsFromRows(rows: readonly EditRow[]): TileEdits {
  const m: TileEdits = new Map();
  for (const [t, dh, pinH, pinBiome, dt, dr, lu] of rows) {
    const e: TileEdit = {};
    if (dh !== null) e.dh = dh;
    if (pinH !== null) e.pinH = pinH;
    if (pinBiome !== null) e.pinBiome = pinBiome;
    if (dt != null) e.dt = dt;
    if (dr != null) e.dr = dr;
    if (lu != null) e.lu = lu;
    m.set(t, e);
  }
  return m;
}
