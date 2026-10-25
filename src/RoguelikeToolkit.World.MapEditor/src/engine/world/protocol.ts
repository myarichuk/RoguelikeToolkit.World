// The contract between the editor (main thread) and the world worker (.NET Core in wasm).
// Plain data only: everything crosses postMessage, big arrays as transferables.
import type { PackedEdits } from './edits/edits';
import type { Rasters } from './rasterize';

export interface NameStyle { race: string; culture: string; label: string; }

/** Kinds of map features the editor can select, filter, label and inspect. */
export type FeatureKind =
  | 'capital' | 'city' | 'town' | 'village' | 'fort'
  | 'ruin' | 'shrine' | 'battlefield'
  | 'river' | 'lake' | 'sea' | 'ocean'
  | 'mountain' | 'valley' | 'forest'
  | 'deposit';

export const FEATURE_KINDS: readonly FeatureKind[] = [
  'capital', 'city', 'town', 'village', 'fort', 'ruin', 'shrine', 'battlefield', 'river', 'lake', 'sea', 'ocean', 'mountain', 'valley', 'forest', 'deposit',
];

/** The kinds that are places: a tile of their own that history events are logged against. */
export const PLACE_KINDS: ReadonlySet<FeatureKind> = new Set<FeatureKind>(['capital', 'city', 'town', 'village', 'fort', 'ruin', 'shrine', 'battlefield']);

/** Name generator kind for a feature kind (Core's PlaceKinds); deposits are not named. */
export const NAME_KIND: Record<Exclude<FeatureKind, 'deposit'>, string> = {
  capital: 'city', city: 'city', town: 'town', village: 'village', fort: 'hold',
  ruin: 'village', shrine: 'hold', battlefield: 'camp',
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
  /** The tile the feature is anchored to: a settlement's or site's own tile, which history events are keyed by. */
  tile: number;
  /** The turns the place can be seen on a history timeline; absent for features that always are. */
  span?: import('./history/timeline').Span;
  /** Area features: member tiles (pixel membership goes through the tile raster). */
  tiles?: Int32Array;
  /** River features: index into FeatureSet.rivers. */
  river?: number;
  deposit?: { type: DepositType; richness: number };
  /** Inspector facts, already formatted: [label, value]. */
  details: [string, string][];
  request?: NameRequest;
  /** Architectural style of the realm that holds (or held) the place, from its culture's data; absent = the default look. */
  architecture?: string;
}

export interface RiverGeometry {
  /** x, y, width triples in map units, upstream -> downstream, x unwrapped (may leave [0, MAP_W)). */
  points: Float32Array;
}

export interface RoadGeometry {
  /** x, y, width triples in map units along the road, x unwrapped (may leave [0, MAP_W)). */
  points: Float32Array;
  /** 'trunk' (a route between capitals, no keeper) or 'spur' (kept by a polity); a plugin's roads arrive as one of these. */
  kind: string;
  /** Keeper polity id, -1 for a trunk route. */
  keeper: number;
  /** Turn the road was cut (war), -1 while intact. */
  severed: number;
  /** Turn the road was built (a time scrubber shows it from then until `severed`). */
  founded: number;
  /** The route's end tiles (the smaller then the larger). */
  from: number; to: number;
}

export interface FeatureSet { features: MapFeature[]; rivers: RiverGeometry[]; roads: RoadGeometry[]; }

export interface WorldSnapshot {
  seed: number; size: number; plates: number; tileCount: number;
  rasters: Rasters;
  /** Per tile: plate id (low 16 bits) | boundary type << 16 | crust << 24. */
  tilePlate: Uint32Array;
  /** Per tile: the name style (index into `styles`) that names places there; see `tileNamingStyles`. */
  tileStyle: Uint8Array;
  /** Per plate (id - 1): drift unit xyz + speed. */
  plateDrift: Float64Array;
  /** Per tile map position (x, y). */
  tileXY: Float32Array;
  /** Per tile land capacity, five bytes each (world/capacity.ts). */
  capacity: Uint8Array;
  /** Per tile sea ice, one byte each: 0 none, 1 seasonal, 2 perennial (Core's SeaIceKind). */
  seaIce: Uint8Array;
  /** Per tile climate, nine floats each (world/climate.ts): temperature, rainfall, wind and current as east/north, ocean heat, seasons. */
  climate: Float32Array;
  features: FeatureSet;
  /** The realms, event log and territory a history run left; null when history is off. */
  history: import('./history/record').HistoryRecord | null;
  styles: NameStyle[];
  timings: Record<string, number>;
}

/** The planet's astronomy (Core's PlanetParameters). Earth by default; every field is a difference-from-Earth knob. */
export interface PlanetSettings {
  /** Axial tilt in degrees, 0..90. */
  obliquity: number;
  /** Stellar flux relative to Earth's. */
  flux: number;
  /** Spin relative to Earth's. */
  rotation: number;
}

export const EARTH: Readonly<PlanetSettings> = { obliquity: 23.44, flux: 1, rotation: 1 };
export const PLANET_LIMITS = { obliquity: [0, 90], flux: [0.3, 2.5], rotation: [0.25, 4] } as const;

/**
 * How the climate is modelled (Core's `WithClimateRefresh`, `ClimateStage.MeridionalWind`, `OceanHeatStrength`,
 * `OceanRainGain` and `ErosionGenerationStage.AeolianIterations`). The defaults are the engine's calibrated
 * ones, so a world generated before these existed (or a file without them) reproduces exactly.
 */
export interface ClimateModelSettings {
  /** Re-run the climate on the eroded relief before rivers and biomes. */
  climateRefresh: boolean;
  /** Share of the wind that blows along the meridian (Coriolis-turned): 0 is purely zonal. */
  meridionalWind: number;
  /** 0 = the legacy wind (unit speed, hard reversal at cell edges); 1 = speed fades to calm at the belt edges. */
  windCellBlend: number;
  /** Strength of the warm/cold ocean-current temperature effect (0 = off). */
  oceanHeat: number;
  /** Rainfall response at the coast to the current's temperature (0 = off). */
  oceanRain: number;
  /** Wind-erosion passes over arid land (0 = off). */
  aeolian: number;
}

export const CLIMATE_MODEL_DEFAULTS: Readonly<ClimateModelSettings> = { climateRefresh: true, meridionalWind: 0.3, windCellBlend: 0, oceanHeat: 1, oceanRain: 2, aeolian: 4 };
export const CLIMATE_MODEL_LIMITS = { meridionalWind: [0, 0.6], windCellBlend: [0, 1], oceanHeat: [0, 2], oceanRain: [0, 4], aeolian: [0, 8] } as const;

/** One pinned founding polity (see Core's HistoryFounder): a blank folk or culture is drawn, form -1 follows culture affinity and land, null coordinates leave the capital to the site scoring. */
export interface FounderPin { folk: string; culture: string; form: number; lat: number | null; lon: number | null; }

/** How a world's history is run and who founds it. */
export interface HistorySettings {
  enabled: boolean;
  /** Turns simulated and years per turn. */
  turns: number;
  years: number;
  /** Founding polities (pinned founders count toward it). */
  maxPolities: number;
  /** Peoples (`folk`, `culture`) the unpinned founders are drawn from; empty = Core's default roster (human, dwarf). */
  roster: { folk: string; culture: string }[];
  founders: FounderPin[];
}

export const DEFAULT_HISTORY: HistorySettings = { enabled: true, turns: 30, years: 10, maxPolities: 8, roster: [], founders: [] };

/** A people a world can be founded by (the engine's `HistoryPeoples`). `affinity` is `Form:weight` pairs, empty when the land alone decides. */
export interface PeopleOption { folk: string; culture: string; folkName: string; cultureName: string; subsistence: string; affinity: { form: string; weight: number }[]; }

export interface GenerateParams {
  seed: number;
  size: number;
  plates: number;
  /** Absent = Earth. */
  planet?: PlanetSettings;
  /** Absent = the calibrated defaults. */
  model?: ClimateModelSettings;
  /** Name styles (`race|culture`) places are named in (realms pick theirs by folk and culture, other land by landmass); empty = DEFAULT_STYLES. */
  styles: string[];
  /** Absent = DEFAULT_HISTORY. */
  history?: HistorySettings;
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
  | { id: number; type: 'styles' }
  | { id: number; type: 'peoples' };

export type WorkerResponse =
  | { id: number; ok: true; type: 'init'; bootMs: number }
  | { id: number; ok: true; type: 'generate'; world: WorldSnapshot }
  | { id: number; ok: true; type: 'recompute'; world: WorldSnapshot }
  | { id: number; ok: true; type: 'names'; names: string[] }
  | { id: number; ok: true; type: 'styles'; styles: NameStyle[] }
  | { id: number; ok: true; type: 'peoples'; peoples: PeopleOption[]; forms: string[] }
  | { id: number; ok: false; error: string };

/** Transferable buffers of a snapshot (zero-copy handoff). */
export function snapshotTransfer(w: WorldSnapshot): Transferable[] {
  const r = w.rasters;
  const out: Transferable[] = [r.elevation.buffer, r.biome.buffer, r.tile.buffer, r.waterPixels.buffer, w.tilePlate.buffer, w.tileStyle.buffer, w.plateDrift.buffer, w.tileXY.buffer, w.capacity.buffer, w.seaIce.buffer, w.climate.buffer];
  if (w.history) out.push(w.history.ownerBase.buffer, w.history.changes.buffer);
  for (const rv of w.features.rivers) out.push(rv.points.buffer);
  for (const rd of w.features.roads) out.push(rd.points.buffer);
  for (const f of w.features.features) if (f.tiles) out.push(f.tiles.buffer);
  return out;
}
