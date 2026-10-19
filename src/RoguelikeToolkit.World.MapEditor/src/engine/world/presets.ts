import type { ClimateModelSettings, PlanetSettings } from './protocol';
import { CLIMATE_MODEL_DEFAULTS, EARTH } from './protocol';

/** A starting point for the planet: its astronomy and how its climate is modelled, with the story the wizard tells about it. */
export interface PlanetPreset {
  id: string;
  label: string;
  /** One line under the name. */
  blurb: string;
  /** What changes on the map, in plain words: shown under the diagram. */
  effects: string[];
  planet: PlanetSettings;
  model: Partial<ClimateModelSettings>;
  /** Colours for the card's globe: [cold, mild, hot] and the share of the surface that reads wet. */
  look: { tint: [string, string, string]; wet: number };
}

export const PLANET_PRESETS: readonly PlanetPreset[] = [
  {
    id: 'earthlike', label: 'Earthlike', blurb: 'Temperate seasons, belts of desert and rain.',
    effects: ['Polar ice caps and tundra, a temperate middle, deserts about 25° from the equator.', 'The calibrated settings every other preset is measured against.'],
    planet: { ...EARTH }, model: {}, look: { tint: ['#dfe9ee', '#7fae6a', '#d8b36a'], wet: 0.55 },
  },
  {
    id: 'tropical', label: 'Tropical', blurb: 'Warm, wet, barely any seasons.',
    effects: ['A low tilt keeps the sun overhead near the equator all year: wide jungle and savanna.', 'Warmer seas feed heavy rain, so rivers run long and big.', 'Ice shrinks to the poles.'],
    planet: { obliquity: 8, flux: 1.12, rotation: 1 }, model: { oceanHeat: 1.5, oceanRain: 3 }, look: { tint: ['#cfe3d0', '#3f9a58', '#e4c46a'], wet: 0.75 },
  },
  {
    id: 'arid', label: 'Arid', blurb: 'Hot sun, thin rain, wind-scoured land.',
    effects: ['More flux and less rain turn the interiors to desert and badlands.', 'Wind erosion runs longer, so dunes and bare rock spread.', 'Rivers are few and short.'],
    planet: { obliquity: 20, flux: 1.18, rotation: 1 }, model: { oceanHeat: 0.9, oceanRain: 0.8, aeolian: 6 }, look: { tint: ['#eadcc0', '#c9a45a', '#d68a4a'], wet: 0.3 },
  },
  {
    id: 'iceage', label: 'Ice age', blurb: 'A dim sun and long winters.',
    effects: ['Less flux pushes snow and tundra toward the equator.', 'Cold seas give little rain: the habitable belt is narrow.', 'Glaciers and cold desert where the rain stops.'],
    planet: { obliquity: 28, flux: 0.78, rotation: 1 }, model: { oceanHeat: 0.6, oceanRain: 1.5 }, look: { tint: ['#f2f6f9', '#a9c4d4', '#8fae8a'], wet: 0.5 },
  },
  {
    id: 'seasons', label: 'Violent seasons', blurb: 'A steep tilt: summers scorch, winters freeze.',
    effects: ['At a 58° tilt every latitude sees the sun swing high and low.', 'Climate bands smear: no stable equatorial belt, big temperature swings.'],
    planet: { obliquity: 58, flux: 1, rotation: 1 }, model: {}, look: { tint: ['#e6edf2', '#9db86a', '#d9944a'], wet: 0.5 },
  },
  {
    id: 'slow', label: 'Slow spinner', blurb: 'Long days: weak winds, lopsided rain.',
    effects: ['A slow spin weakens the wind belts; the tropics turn into one big circulation.', 'Rain concentrates on coasts facing the wind.'],
    planet: { obliquity: 15, flux: 1, rotation: 0.35 }, model: { windCellBlend: 0.5 }, look: { tint: ['#e0e8ee', '#8fb27a', '#d0a860'], wet: 0.45 },
  },
  {
    id: 'fast', label: 'Fast spinner', blurb: 'Short days: tight, stripy climate bands.',
    effects: ['A fast spin splits the winds into many narrow belts.', 'Alternating wet and dry stripes run east to west.'],
    planet: { obliquity: 23.44, flux: 1, rotation: 2.5 }, model: { windCellBlend: 0.3, meridionalWind: 0.2 }, look: { tint: ['#dde8ee', '#6fa070', '#cfae62'], wet: 0.5 },
  },
];

export interface TectonicPreset {
  id: string;
  label: string;
  blurb: string;
  /** Tectonic plates: fewer make big continents, more make islands and many ranges. */
  plates: number;
  /** Wind erosion passes (0-8): more wears ranges down and fills basins. */
  aeolian: number;
}

export const TECTONIC_PRESETS: readonly TectonicPreset[] = [
  { id: 'super', label: 'Supercontinent', blurb: 'A few huge plates: one vast landmass, long mountain chains where they meet.', plates: 5, aeolian: 3 },
  { id: 'earth', label: 'Earthlike', blurb: 'A dozen or so plates: several continents and seas between them.', plates: 14, aeolian: 4 },
  { id: 'archipelago', label: 'Archipelago', blurb: 'Many small plates: scattered islands and short ranges.', plates: 30, aeolian: 4 },
  { id: 'fractured', label: 'Fractured', blurb: 'A crowd of tiny plates: ragged coasts and mountains everywhere.', plates: 40, aeolian: 2 },
  { id: 'ancient', label: 'Ancient and worn', blurb: 'Few plates and long erosion: rounded hills, broad plains.', plates: 9, aeolian: 8 },
];

/** Applies a planet preset on top of the calibrated climate defaults (a preset only lists what it changes). */
export const modelFor = (p: PlanetPreset): ClimateModelSettings => ({ ...CLIMATE_MODEL_DEFAULTS, ...p.model });

/** Detail levels the wizard offers, with how many tiles each has (Core's icosphere: 10 * 4^n + 2). */
export const DETAIL_LEVELS = [
  { size: 4, label: 'Sketch', note: 'Quick: coarse coasts, good for trying ideas.' },
  { size: 5, label: 'Standard', note: 'The default: about 10 thousand tiles.' },
  { size: 6, label: 'Fine', note: 'Four times the tiles: sharper coasts and rivers, several times slower.' },
  { size: 7, label: 'Huge', note: 'Sixteen times the tiles. Slow, and heavy on memory: not for phones or old laptops.' },
] as const;

export const tilesOf = (size: number): number => 10 * 4 ** size + 2;

/** Default generation cost (ms per tile) before this device has been timed: measured on a recent laptop. */
export const DEFAULT_MS_PER_TILE = 0.32;

/** Rough wall-clock time to generate a world of this detail, from this device's measured speed when known. */
export const estimateGenerateMs = (size: number, msPerTile = DEFAULT_MS_PER_TILE): number => Math.round(tilesOf(size) * msPerTile);

/** "about 4 s", "about 2 min": a round figure that does not pretend to precision. */
export function formatEstimate(ms: number): string {
  if (ms < 1500) return 'about a second';
  if (ms < 90_000) return `about ${Math.round(ms / 1000)} s`;
  return `about ${Math.round(ms / 60_000)} min`;
}

/** The preset a set of planet settings equals, or null when it is custom. */
export function matchPlanetPreset(planet: PlanetSettings, model: ClimateModelSettings): PlanetPreset | null {
  return PLANET_PRESETS.find((p) => {
    const m = modelFor(p);
    return p.planet.obliquity === planet.obliquity && p.planet.flux === planet.flux && p.planet.rotation === planet.rotation
      && (Object.keys(m) as (keyof ClimateModelSettings)[]).every((k) => m[k] === model[k]);
  }) ?? null;
}
