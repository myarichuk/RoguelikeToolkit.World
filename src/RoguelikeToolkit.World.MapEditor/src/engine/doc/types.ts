// Document entities. Everything is in map units (see core/geo.ts) and plain JSON-serializable.

export interface Entity { id: string; }

export interface StampEntity extends Entity {
  /** Content pack asset id ("core.mountain"). */
  asset: string;
  variant: number;
  x: number; y: number;
  /** Multiplier on the asset's default size. */
  scale: number;
  /** Radians. */
  rotation: number;
  flip: boolean;
}

export interface LabelEntity extends Entity {
  text: string;
  /** Label style key from the content packs ("city", "sea", ...). */
  style: string;
  x: number; y: number;
  /** Baseline direction, radians. */
  angle: number;
  /** Cap height in map units; 0 = style default. */
  size: number;
  /** Extra letter spacing in em; NaN = style default. */
  spacing: number;
  /** Arc bend: 0 straight, ±1 strong. Ignored when `path` is set. */
  curve: number;
  /** Optional baseline polyline (x, y pairs, absolute) the text follows, e.g. a river. */
  path?: number[];
  /** What the label names, for re-rolling names and keeping labels with their feature. */
  feature?: { kind: string; request?: { kind: string; seed: number; race: string; culture: string; context: number } };
}

export interface RiverEntity extends Entity {
  name: string;
  /** x, y, width triples, upstream -> downstream; x may run past the map edge (unwrapped). */
  points: number[];
}

/** A road or track drawn on the map: history's trade routes and spurs, or any plugin's `AddRoad`. */
export interface RoadEntity extends Entity {
  /** 'trunk' (between capitals) or 'spur' (kept by a polity). */
  kind: string;
  /** Keeper polity id, -1 for a trunk route. */
  keeper: number;
  /** Turn a war cut it, -1 while intact. */
  severed: number;
  /** Turn it was built; with `severed` it brackets when the road stands on a history timeline. */
  founded?: number;
  /** x, y, width triples along the road; x may run past the map edge (unwrapped). */
  points: number[];
}

export interface DocSettings {
  seed: number;
  size: number;
  plates: number;
  /** Coastline stylization: domain warp amplitude (map units) and detail noise amplitude (height units). */
  warp: number;
  detail: number;
  /** Ripple ring spacing (map units). */
  ring: number;
  /** Name styles used for cultures (`race|culture`). */
  styles: string[];
  title: string;
  /** The planet's axial tilt (degrees), stellar flux and spin, relative to Earth. Part of what identifies the generated world. */
  obliquity: number;
  flux: number;
  rotation: number;
  /** How the climate was modelled (see ClimateModelSettings); part of what identifies the generated world. Absent in old files = the defaults. */
  climateRefresh: boolean;
  meridionalWind: number;
  windCellBlend: number;
  oceanHeat: number;
  oceanRain: number;
  aeolian: number;
  /** How history was run and who founded it; absent in old files = the defaults. Part of what identifies the generated world. */
  history?: import('../world/protocol').HistorySettings;
}

export const DEFAULT_SETTINGS: DocSettings = {
  seed: 42, size: 5, plates: 12, warp: 6, detail: 0.05, ring: 2.5, styles: [], title: 'Untitled world',
  obliquity: 23.44, flux: 1, rotation: 1,
  climateRefresh: true, meridionalWind: 0.3, windCellBlend: 0, oceanHeat: 1, oceanRain: 2, aeolian: 4,
};

/** The label and the settlement stamp that belong to a feature are named after it. */
export const labelIdOf = (featureId: string): string => `lb_${featureId}`;
export const stampIdOf = (featureId: string): string => `st_${featureId}`;
/** The feature a label or a settlement stamp belongs to, when its id says so (what the user placed by hand has none). */
export const featureIdOfLabel = (labelId: string): string | undefined => (labelId.startsWith('lb_f_') ? labelId.slice(3) : undefined);
export const featureIdOfStamp = (stampId: string): string | undefined => (stampId.startsWith('st_f_') ? stampId.slice(3) : undefined);

/** A named, selectable map feature (river, range, sea, settlement, deposit...). */
export interface FeatureEntity extends Entity {
  kind: import('../world/protocol').FeatureKind;
  name: string;
  x: number; y: number;
  angle: number; extent: number;
  weight: number;
  /** The tile the feature is anchored to (history events are keyed by it); absent in files saved before it was recorded. */
  tile?: number;
  /** The turns the place stood on a history timeline; absent for features that always are (and in older files). */
  span?: import('../world/history/timeline').Span;
  /** Area membership by tile; pixels map to tiles through the document's tile raster. */
  tiles?: number[];
  /** River features: the river entity drawn for it. */
  riverId?: string;
  deposit?: { type: import('../world/protocol').DepositType; richness: number };
  details: [string, string][];
  culture: string;
  request?: import('../world/protocol').NameRequest;
  /** Architectural style its marker is drawn in (the holding realm's culture); absent = the default look, and in older files. */
  architecture?: string;
}
