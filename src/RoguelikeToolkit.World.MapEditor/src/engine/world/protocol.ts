// The contract between the editor (main thread) and the world worker (.NET Core in wasm).
// Plain data only: everything crosses postMessage, big arrays as transferables.
import type { PackedEdits } from './edits';
import type { Rasters } from './rasterize';

export interface NameStyle { race: string; culture: string; label: string; }

/** Kinds of map features the editor can select, filter, label and inspect. */
export type FeatureKind =
  | 'capital' | 'city' | 'town' | 'village'
  | 'river' | 'lake' | 'sea' | 'ocean'
  | 'mountain' | 'valley' | 'forest'
  | 'deposit';

export const FEATURE_KINDS: readonly FeatureKind[] = [
  'capital', 'city', 'town', 'village', 'river', 'lake', 'sea', 'ocean', 'mountain', 'valley', 'forest', 'deposit',
];

/** Name generator kind for a feature kind (Core's PlaceKinds); deposits are not named. */
export const NAME_KIND: Record<Exclude<FeatureKind, 'deposit'>, string> = {
  capital: 'city', city: 'city', town: 'town', village: 'village',
  river: 'river', lake: 'lake', sea: 'sea', ocean: 'sea',
  mountain: 'mountain', valley: 'valley', forest: 'forest',
};

/** Core's DepositType, by value. */
export const DEPOSIT_TYPES = ['iron', 'copper', 'gold', 'silver', 'tin', 'leadZinc', 'uranium', 'coal', 'oil', 'gas', 'salt', 'gems', 'bauxite', 'gypsum'] as const;
export type DepositType = (typeof DEPOSIT_TYPES)[number];

/** Name context bits understood by the engine's NamePlaces. */
export const NameCtx = { Water: 1, Forest: 2, DarkForest: 4, Highland: 8, Valley: 16, Deposit: 32, Danger: 64 } as const;

export interface NameRequest { kind: string; seed: number; race: string; culture: string; context: number; }

export interface MapFeature {
  /** Stable within a world: derived from Core ids/tiles, so regeneration maps onto the same keys. */
  key: string;
  kind: FeatureKind;
  name: string;
  /** Anchor in map units. */
  x: number; y: number;
  /** Principal axis angle (radians, map space) and half-extent along it, for area labels. */
  angle: number; extent: number;
  /** Larger = more important (label size and placement priority). */
  weight: number;
  /** Index into WorldSnapshot.styles (the culture naming this feature). */
  style: number;
  /** Area features: member tiles (pixel membership goes through the tile raster). */
  tiles?: Int32Array;
  /** River features: index into FeatureSet.rivers. */
  river?: number;
  deposit?: { type: DepositType; richness: number };
  /** Inspector facts, already formatted: [label, value]. */
  details: [string, string][];
  request?: NameRequest;
}

export interface RiverGeometry {
  /** x, y, width triples in map units, upstream -> downstream, x unwrapped (may leave [0, MAP_W)). */
  points: Float32Array;
}

export interface FeatureSet { features: MapFeature[]; rivers: RiverGeometry[]; }

export interface WorldSnapshot {
  seed: number; size: number; plates: number; tileCount: number;
  rasters: Rasters;
  /** Per tile: plate id (low 16 bits) | boundary type << 16 | crust << 24. */
  tilePlate: Uint32Array;
  /** Per plate (id - 1): drift unit xyz + speed. */
  plateDrift: Float64Array;
  /** Per tile map position (x, y). */
  tileXY: Float32Array;
  features: FeatureSet;
  styles: NameStyle[];
  timings: Record<string, number>;
}

export interface GenerateParams {
  seed: number;
  size: number;
  plates: number;
  /** Name styles (`race|culture`) cultures are drawn from, one per plate; empty = DEFAULT_STYLES. */
  styles: string[];
}

export interface RecomputeParams extends GenerateParams {
  /** The whole hand-edit table (absolute, not a delta): the engine keeps no history. */
  edits: PackedEdits;
  /** Rasters only: no feature extraction or naming (used to rebuild a baseline). */
  rastersOnly: boolean;
}

/** A mixed fantasy world out of the box; the editor lets the user pick any engine style. */
export const DEFAULT_STYLES = [
  'human|fantasy_common', 'human|human_western', 'human|human_welsh', 'human|human_irish',
  'elf_wood|fantasy_common', 'dwarf|fantasy_common', 'orc|fantasy_common', 'human|human_english',
];

export const styleKey = (s: NameStyle): string => `${s.race}|${s.culture}`;

export type WorkerRequest =
  | { id: number; type: 'init'; frameworkUrl: string }
  | { id: number; type: 'generate'; params: GenerateParams }
  | { id: number; type: 'recompute'; params: RecomputeParams }
  | { id: number; type: 'names'; requests: NameRequest[] }
  | { id: number; type: 'styles' };

export type WorkerResponse =
  | { id: number; ok: true; type: 'init'; bootMs: number }
  | { id: number; ok: true; type: 'generate'; world: WorldSnapshot }
  | { id: number; ok: true; type: 'recompute'; world: WorldSnapshot }
  | { id: number; ok: true; type: 'names'; names: string[] }
  | { id: number; ok: true; type: 'styles'; styles: NameStyle[] }
  | { id: number; ok: false; error: string };

/** Transferable buffers of a snapshot (zero-copy handoff). */
export function snapshotTransfer(w: WorldSnapshot): Transferable[] {
  const r = w.rasters;
  const out: Transferable[] = [r.elevation.buffer, r.biome.buffer, r.tile.buffer, w.tilePlate.buffer, w.plateDrift.buffer, w.tileXY.buffer];
  for (const rv of w.features.rivers) out.push(rv.points.buffer);
  for (const f of w.features.features) if (f.tiles) out.push(f.tiles.buffer);
  return out;
}
