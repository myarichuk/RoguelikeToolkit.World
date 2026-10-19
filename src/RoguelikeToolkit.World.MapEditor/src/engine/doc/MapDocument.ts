import { Emitter } from '../core/Emitter';
import { MAP_H, MAP_W, type Rect } from '../core/geo';
import type { RasterTarget } from '../commands/RasterEdit';
import type { NameStyle } from '../world/protocol';
import type { Raster, TileEdits } from '../world/edits';
import { featureMask, maskHas, tileRowSpans, type FeatureMask } from '../features/featureMask';
import { EntityStore } from './EntityStore';
import { DEFAULT_SETTINGS, type DocSettings, type FeatureEntity, type LabelEntity, type RiverEntity, type RoadEntity, type StampEntity } from './types';

export type RasterLayer = 'elevation' | 'biome' | 'warmth' | 'wetness' | 'landuse';

/** Read-only reference data from generation: what the planet *is*, as opposed to what was drawn. */
export interface WorldReference {
  /** Dominant tile per pixel (feature membership, inspection). */
  tile: Int32Array;
  /** Per tile: plate id | boundary << 16 | crust << 24. */
  tilePlate: Uint32Array;
  /** Per plate: drift xyz + speed. */
  plateDrift: Float64Array;
  /** Per tile map position. */
  tileXY: Float32Array;
  styles: NameStyle[];
}

/**
 * What the physics knows about this document's hand edits (see world/edits.ts). The edit table is
 * the persistent part; the baseline is a cache of what the engine produced for it.
 */
export interface PhysicsState {
  /** Sparse per-tile edits the world is derived from (with the seed, the whole truth about the terrain). */
  edits: TileEdits;
  /** The rasters the engine produced for `edits`; null until needed (rebuilt deterministically from the table). */
  baseline: Raster | null;
  /** Ids of features that came from the engine, as opposed to hand-made ones (river tool...). */
  generated: Set<string>;
  /** Engine features the author deleted, with their geometry, so they stay deleted when the world is re-derived. */
  tombstones: Map<string, FeatureEntity>;
  /** Land capacity per tile (world/capacity.ts) as the engine last computed it; absent in files from before it existed. */
  capacity?: Uint8Array;
  /** Sea ice per tile (0 none, 1 seasonal, 2 perennial) as the engine last computed it; absent in files from before it existed. */
  seaIce?: Uint8Array;
  /** Climate per tile (world/climate.ts), as the engine last computed it; absent in files from before it existed. */
  climate?: Float32Array;
}

/** Engine feature keys look like `f_lake:3`; hand-made ones (`f_river_x7k2`) never contain a colon. */
export const isGeneratedFeatureId = (id: string): boolean => /^f_[a-z]+:/.test(id);

/**
 * The aggregate root of a map: editable rasters, entity stores, settings and world reference
 * data. It knows nothing about rendering, tools or UI; those observe its events.
 */
export class MapDocument {
  readonly width = MAP_W;
  readonly height = MAP_H;
  readonly features = new EntityStore<FeatureEntity>();
  readonly stamps = new EntityStore<StampEntity>();
  readonly labels = new EntityStore<LabelEntity>();
  readonly rivers = new EntityStore<RiverEntity>();
  readonly roads = new EntityStore<RoadEntity>();
  readonly events = new Emitter<{ raster: { layer: RasterLayer; rect: Rect }; settings: DocSettings; reset: MapDocument }>();
  private featureByTile: Map<number, string[]> | null = null;
  private readonly masks = new Map<string, FeatureMask | null>();
  private tileRows: Int32Array | null = null;
  /** Null for hand-made maps (no engine world behind them), so there is nothing to re-derive. */
  physics: PhysicsState | null = null;
  /**
   * Climate paint: signed offsets the author brushed, -127..127 per texel (full scale is CLIMATE_RANGE in
   * world/edits.ts). Inputs, not derived: the physics never overwrites them, only "Revert" clears them.
   */
  readonly warmth = new Int8Array(MAP_W * MAP_H);
  readonly wetness = new Int8Array(MAP_W * MAP_H);
  /**
   * Land use per texel (0 none; ids in world/landuse.ts): farmland, ashlands. Hand-painted decoration over the
   * biome, not an input to the physics, so it survives Apply and Revert untouched.
   */
  readonly landuse = new Uint8Array(MAP_W * MAP_H);

  constructor(
    public settings: DocSettings,
    public readonly elevation: Float32Array,
    public readonly biome: Uint8Array,
    public readonly world: WorldReference,
  ) {
    this.features.events.on('change', () => { this.featureByTile = null; this.masks.clear(); });
    this.events.on('raster', ({ layer }) => { if (layer === 'elevation' || layer === 'biome') this.masks.clear(); });
  }

  static empty(): MapDocument {
    return new MapDocument({ ...DEFAULT_SETTINGS }, new Float32Array(MAP_W * MAP_H).fill(-0.2), new Uint8Array(MAP_W * MAP_H),
      { tile: new Int32Array(MAP_W * MAP_H).fill(-1), tilePlate: new Uint32Array(0), plateDrift: new Float64Array(0), tileXY: new Float32Array(0), styles: [] });
  }

  /** A raster as an undo/redo target that keeps renderers informed. */
  target(layer: 'elevation'): RasterTarget<Float32Array>;
  target(layer: 'biome' | 'landuse'): RasterTarget<Uint8Array>;
  target(layer: 'warmth' | 'wetness'): RasterTarget<Int8Array>;
  target(layer: RasterLayer): RasterTarget<Float32Array> | RasterTarget<Uint8Array> | RasterTarget<Int8Array> {
    const data = layer === 'elevation' ? this.elevation : layer === 'biome' ? this.biome : layer === 'landuse' ? this.landuse : layer === 'warmth' ? this.warmth : this.wetness;
    return { data: data as never, width: this.width, height: this.height, touched: (rect) => this.events.emit('raster', { layer, rect }) };
  }

  /** True when the pixels map to an engine world, i.e. physics can be re-derived from edits. */
  get hasWorld(): boolean { return this.world.tileXY.length > 0; }

  /** Overwrites both rasters in place (renderers hold these arrays) and tells them everything changed. */
  replaceRasters(elevation: Float32Array, biome: Uint8Array): void {
    this.elevation.set(elevation);
    this.biome.set(biome);
    const rect = { x0: 0, y0: 0, x1: this.width, y1: this.height };
    this.events.emit('raster', { layer: 'elevation', rect });
    this.events.emit('raster', { layer: 'biome', rect });
  }

  /** Overwrites the climate paint in place (undo of a revert, file load) and tells the overlay. */
  replaceClimate(warmth: Int8Array, wetness: Int8Array): void {
    this.warmth.set(warmth);
    this.wetness.set(wetness);
    const rect = { x0: 0, y0: 0, x1: this.width, y1: this.height };
    this.events.emit('raster', { layer: 'warmth', rect });
    this.events.emit('raster', { layer: 'wetness', rect });
  }

  /** True when any texel carries climate paint. */
  get hasClimatePaint(): boolean {
    for (let i = 0; i < this.warmth.length; i++) if (this.warmth[i] !== 0 || this.wetness[i] !== 0) return true;
    return false;
  }

  setSettings(patch: Partial<DocSettings>): void {
    this.settings = { ...this.settings, ...patch };
    this.events.emit('settings', this.settings);
  }

  /** Pixel -> tile (or -1 off-world / for hand-made documents). */
  tileAt(x: number, y: number): number {
    const xi = ((Math.floor(x) % this.width) + this.width) % this.width, yi = Math.floor(y);
    if (yi < 0 || yi >= this.height) return -1;
    return this.world.tile[yi * this.width + xi];
  }

  /** Area features owning a tile (rivers excluded). */
  featuresOnTile(t: number): FeatureEntity[] {
    if (!this.featureByTile) {
      const m = new Map<number, string[]>();
      for (const f of this.features.all()) {
        if (!f.tiles || f.kind === 'river') continue;
        for (const tile of f.tiles) { let l = m.get(tile); if (!l) m.set(tile, (l = [])); l.push(f.id); }
      }
      this.featureByTile = m;
    }
    return (this.featureByTile.get(t) ?? []).map((id) => this.features.get(id)!).filter(Boolean);
  }

  /** An area feature's footprint as drawn (see features/featureMask.ts), cached until its rasters or the features change. */
  maskOf(id: string): FeatureMask | null {
    if (!this.masks.has(id)) {
      const f = this.features.get(id);
      this.tileRows ??= tileRowSpans(this.world.tile, this.width, this.world.tilePlate.length);
      this.masks.set(id, f ? featureMask({ width: this.width, height: this.height, elevation: this.elevation, biome: this.biome, tile: this.world.tile, owners: (t) => this.featuresOnTile(t), rows: this.tileRows }, f) : null);
    }
    return this.masks.get(id)!;
  }

  /**
   * Area features containing a pixel as drawn: the owners of the tiles around it, kept when their
   * drawn footprint covers it (a shore pixel may sit on a land tile, a lake no longer fills its tile).
   */
  featuresAt(x: number, y: number): FeatureEntity[] {
    if (this.tileAt(x, y) < 0) return [];
    const seen = new Set<string>(), out: FeatureEntity[] = [];
    for (let dy = -16; dy <= 16; dy += 8) {
      for (let dx = -16; dx <= 16; dx += 8) {
        const t = this.tileAt(x + dx, y + dy);
        if (t < 0) continue;
        for (const f of this.featuresOnTile(t)) {
          if (seen.has(f.id)) continue;
          seen.add(f.id);
          const m = this.maskOf(f.id);
          if (m && maskHas(m, x, y)) out.push(f);
        }
      }
    }
    return out;
  }

  elevationAt(x: number, y: number): number {
    const xi = ((Math.floor(x) % this.width) + this.width) % this.width, yi = Math.max(0, Math.min(this.height - 1, Math.floor(y)));
    return this.elevation[yi * this.width + xi];
  }

  landUseAt(x: number, y: number): number {
    const xi = ((Math.floor(x) % this.width) + this.width) % this.width;
    const yi = Math.min(this.height - 1, Math.max(0, Math.floor(y)));
    return this.landuse[yi * this.width + xi];
  }

  biomeAt(x: number, y: number): number {
    const xi = ((Math.floor(x) % this.width) + this.width) % this.width, yi = Math.max(0, Math.min(this.height - 1, Math.floor(y)));
    return this.biome[yi * this.width + xi];
  }
}
