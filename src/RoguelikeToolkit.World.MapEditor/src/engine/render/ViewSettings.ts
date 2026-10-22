import type { CapacityKind } from '../world/physics/capacity';
import type { ClimateKind } from '../world/physics/climate';
import { DEPOSIT_TYPES, FEATURE_KINDS, type DepositType, type FeatureKind } from '../world/protocol';

export type BaseMode = 'styled' | 'biomes' | 'elevation' | 'plates';

export interface Layers {
  hillshade: boolean;
  contours: boolean;
  plateBoundaries: boolean;
  plateMotion: boolean;
  graticule: boolean;
  ripples: boolean;
  coastline: boolean;
  rivers: boolean;
  roads: boolean;
  stamps: boolean;
  labels: boolean;
  resources: boolean;
  /** Tint of the temperature / rainfall offsets painted with the climate brush. */
  climatePaint: boolean;
  /** Tint of what the land could carry (farming, grazing, game, timber, fish), from the engine's capacity. */
  capacity: boolean;
  /** Tint of the engine's climate (temperature, rainfall, currents or seasons: see `climateKind`). */
  climate: boolean;
  /** Arrows for the prevailing wind. */
  wind: boolean;
  /** Arrows for the ocean surface currents, warm ones red and cold ones blue. */
  currents: boolean;
  /** Tint of who held each tile, at the history turn the scrubber shows. */
  realms: boolean;
  /** Realm outlines and names at the same turn. */
  borders: boolean;
}

/**
 * What is shown, independent of what the document contains and of the theme. Feature and
 * deposit filters gate visibility *and* selectability: a hidden thing cannot be picked.
 */
export interface ViewSettings {
  base: BaseMode;
  /** Plates view: 0 = flat plate colors, 1 = plates tinted over topography. */
  plateTopo: number;
  /** The history turn the realms layer shows; -1 is the end of history. */
  historyTurn: number;
  /** Which capacity the `capacity` layer shows. */
  capacityKind: CapacityKind;
  /** Which climate field the `climate` layer shows. */
  climateKind: ClimateKind;
  layers: Layers;
  kinds: Record<FeatureKind, boolean>;
  deposits: Record<DepositType, boolean>;
}

const allKinds = (v: boolean) => Object.fromEntries(FEATURE_KINDS.map((k) => [k, v])) as Record<FeatureKind, boolean>;
const allDeposits = (v: boolean) => Object.fromEntries(DEPOSIT_TYPES.map((k) => [k, v])) as Record<DepositType, boolean>;

const LAYERS_OFF: Layers = {
  hillshade: false, contours: false, plateBoundaries: false, plateMotion: false, graticule: false,
  ripples: false, coastline: false, rivers: false, roads: false, stamps: false, labels: false, resources: false, climatePaint: false, capacity: false, climate: false, wind: false, currents: false, realms: false, borders: false,
};

export const DEFAULT_VIEW: ViewSettings = {
  base: 'styled', plateTopo: 0, historyTurn: -1, capacityKind: 'arable', climateKind: 'temperature',
  layers: { ...LAYERS_OFF, ripples: true, rivers: true, roads: true, stamps: true, labels: true },
  kinds: { ...allKinds(true), deposit: false },
  deposits: allDeposits(true),
};

export interface ViewPreset { id: string; label: string; apply(v: ViewSettings): ViewSettings; }

/** Named looks. Presets set the base mode and layers but keep the user's feature filters. */
export const VIEW_PRESETS: ViewPreset[] = [
  { id: 'map', label: 'Map', apply: (v) => ({ ...v, base: 'styled', layers: { ...LAYERS_OFF, ripples: true, rivers: true, roads: true, stamps: true, labels: true } }) },
  { id: 'plates', label: 'Plates', apply: (v) => ({ ...v, base: 'plates', plateTopo: 0, layers: { ...LAYERS_OFF, plateBoundaries: true, plateMotion: true, coastline: true } }) },
  { id: 'plates-topo', label: 'Plates + topography', apply: (v) => ({ ...v, base: 'plates', plateTopo: 0.75, layers: { ...LAYERS_OFF, hillshade: true, plateBoundaries: true, plateMotion: true, coastline: true } }) },
  { id: 'realms', label: 'Realms', apply: (v) => ({ ...v, base: 'styled', layers: { ...LAYERS_OFF, ripples: true, rivers: true, roads: true, labels: true, realms: true, borders: true } }) },
  { id: 'topo', label: 'Topography', apply: (v) => ({ ...v, base: 'elevation', layers: { ...LAYERS_OFF, hillshade: true, contours: true, coastline: true, rivers: true } }) },
  { id: 'biomes', label: 'Biomes', apply: (v) => ({ ...v, base: 'biomes', layers: { ...LAYERS_OFF, hillshade: true, coastline: true, rivers: true } }) },
  { id: 'land', label: 'Land capacity', apply: (v) => ({ ...v, base: 'biomes', layers: { ...LAYERS_OFF, hillshade: true, coastline: true, rivers: true, capacity: true } }) },
  { id: 'climate', label: 'Climate', apply: (v) => ({ ...v, base: 'elevation', layers: { ...LAYERS_OFF, hillshade: true, coastline: true, climate: true } }) },
  { id: 'air', label: 'Winds & currents', apply: (v) => ({ ...v, base: 'elevation', layers: { ...LAYERS_OFF, hillshade: true, coastline: true, climate: true, wind: true, currents: true }, climateKind: 'oceanHeat' }) },
  { id: 'resources', label: 'Resources', apply: (v) => ({ ...v, base: 'elevation', layers: { ...LAYERS_OFF, hillshade: true, coastline: true, rivers: true, labels: true, resources: true }, kinds: { ...v.kinds, deposit: true } }) },
];

/** Display shader flag bits (see FS_DISPLAY). */
export function displayFlags(v: ViewSettings, hasSelectionMask: boolean): number {
  const l = v.layers;
  return (l.hillshade ? 1 : 0) | (l.contours ? 2 : 0) | (l.plateBoundaries ? 4 : 0) | (l.graticule ? 8 : 0)
    | (l.ripples ? 16 : 0) | (l.coastline ? 32 : 0) | (hasSelectionMask ? 64 : 0);
}

export const BASE_MODE_INDEX: Record<BaseMode, number> = { styled: 0, biomes: 1, elevation: 2, plates: 3 };
