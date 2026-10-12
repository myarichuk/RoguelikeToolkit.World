import { CLIMATE_RANGE, PAINT_MAX } from './edits';

// The climate layer as the engine computed it, one record of nine floats per tile. Like land capacity it is
// derived physics: shown, read back by the inspector, never an input (the climate brush is the input).
//
// Wind and ocean current arrive from Core as 3D tangent vectors; the editor draws on a flat map, so they are
// projected once, at the worker boundary, onto the tile's local east and north (north = up the screen).

export type ClimateKind = 'temperature' | 'rainfall' | 'oceanHeat' | 'seasons' | 'rainSeason' | 'seaIce';

/** `ClimateDef.index` of a kind that is not a column of the climate array (sea ice has its own per-tile byte array). */
export const NOT_A_COLUMN = -1;

/** Floats per tile in the snapshot's `climate` array; the order is `CI`. */
export const CLIMATE_STRIDE = 10;
export const CI = { temperature: 0, rainfall: 1, windE: 2, windN: 3, currentE: 4, currentN: 5, oceanHeat: 6, seasonalRange: 7, rainSeasonality: 8, rainPhase: 9 } as const;

/** The stride before `rainPhase` was added: a cached climate array of this width is widened, not thrown away. */
export const LEGACY_CLIMATE_STRIDE = 9;

/** Doubles per tile in the engine's `ClimateField()` (Core's ClimateInfo, field by field). */
export const ENGINE_CLIMATE_STRIDE = 12;

/**
 * A cache saved before the rain phase existed, widened to the current stride. The new column is 0 ("no known season"),
 * which reads as an even year; Apply physics fills it in. Anything else (a different world) is not a cache of this one.
 */
export function widenClimate(cached: Float32Array, tileCount: number): Float32Array | undefined {
  if (cached.length === tileCount * CLIMATE_STRIDE) return cached;
  if (cached.length !== tileCount * LEGACY_CLIMATE_STRIDE) return undefined;
  const out = new Float32Array(tileCount * CLIMATE_STRIDE);
  for (let i = 0; i < tileCount; i++)
    for (let k = 0; k < LEGACY_CLIMATE_STRIDE; k++) out[i * CLIMATE_STRIDE + k] = cached[i * LEGACY_CLIMATE_STRIDE + k];
  return out;
}

/** Core's temperature units are about 55 K per unit (ClimateModel); the equator at sea level is 1 and the poles are 0. */
export const KELVIN_PER_UNIT = 55;

/**
 * Engine climate (twelve doubles per tile) -> editor climate (ten floats per tile), with wind and current
 * projected onto east / north at each tile's position. `vectors` are the unit tile centres, xyz interleaved, z up.
 */
export function projectClimate(raw: ArrayLike<number>, vectors: ArrayLike<number>, tileCount: number): Float32Array {
  const out = new Float32Array(tileCount * CLIMATE_STRIDE);
  for (let i = 0; i < tileCount; i++) {
    const r = i * ENGINE_CLIMATE_STRIDE, o = i * CLIMATE_STRIDE;
    const x = vectors[i * 3], y = vectors[i * 3 + 1], z = vectors[i * 3 + 2];
    const h = Math.hypot(x, y);
    // At the poles east and north are undefined; a zero basis draws nothing rather than a wrong arrow.
    const ex = h > 1e-9 ? -y / h : 0, ey = h > 1e-9 ? x / h : 0;
    const nx = h > 1e-9 ? (-x * z) / h : 0, ny = h > 1e-9 ? (-y * z) / h : 0, nz = h > 1e-9 ? h : 0;
    out[o + CI.temperature] = raw[r]; out[o + CI.rainfall] = raw[r + 1];
    out[o + CI.windE] = raw[r + 2] * ex + raw[r + 3] * ey;
    out[o + CI.windN] = raw[r + 2] * nx + raw[r + 3] * ny + raw[r + 4] * nz;
    out[o + CI.seasonalRange] = raw[r + 5]; out[o + CI.rainSeasonality] = raw[r + 6]; out[o + CI.oceanHeat] = raw[r + 7]; out[o + CI.rainPhase] = raw[r + 11];
    out[o + CI.currentE] = raw[r + 8] * ex + raw[r + 9] * ey;
    out[o + CI.currentN] = raw[r + 8] * nx + raw[r + 9] * ny + raw[r + 10] * nz;
  }
  return out;
}

type Stop = readonly [pos: number, r: number, g: number, b: number];

export interface ClimateDef {
  key: ClimateKind; label: string; hint: string;
  index: number;
  /** The value range the ramp spans. */
  lo: number; hi: number;
  stops: readonly Stop[];
  /** Legend end labels, low then high. */
  legend: readonly [string, string];
}

export const CLIMATE_KINDS: readonly ClimateDef[] = [
  {
    key: 'temperature', label: 'Temp', index: CI.temperature, lo: 0, hi: 1, legend: ['cold', 'hot'],
    hint: 'Annual-mean temperature after latitude, height, the planet\'s tilt and warm or cold sea currents. Shown as the engine\'s 0 to 1 index (0 polar, 1 hot equatorial lowland); one unit is roughly 55 K, but it is a model index, not a thermometer reading.',
    stops: [[0, 49, 54, 149], [0.25, 69, 117, 180], [0.45, 171, 217, 233], [0.55, 254, 224, 144], [0.75, 244, 109, 67], [1, 165, 0, 38]],
  },
  {
    key: 'rainfall', label: 'Rain', index: CI.rainfall, lo: 0, hi: 1, legend: ['dry', 'wet'],
    hint: 'How much rain falls in a year, as a 0 to 1 index. Wind carries moisture off the sea, mountains wring it out (leaving rain shadows behind them), and warm currents add some on coasts.',
    stops: [[0, 253, 245, 215], [0.35, 161, 218, 180], [0.7, 65, 182, 196], [1, 34, 94, 168]],
  },
  {
    key: 'oceanHeat', label: 'Currents', index: CI.oceanHeat, lo: -0.15, hi: 0.15, legend: ['cold current', 'warm current'],
    hint: 'How much the sea currents warm (red) or cool (blue) a tile: the Gulf Stream warms Europe, the Humboldt and Benguela currents cool the coasts of Peru and Namibia. Land shows the share a sea breeze carries inland.',
    stops: [[0, 33, 102, 172], [0.5, 247, 247, 247], [1, 178, 24, 43]],
  },
  {
    key: 'seasons', label: 'Seasons', index: CI.seasonalRange, lo: 0, hi: 0.5, legend: ['mild year', 'harsh swing'],
    hint: 'How far the temperature swings between summer and winter. Largest deep inland and far from the equator; a bigger axial tilt makes it larger everywhere.',
    stops: [[0, 242, 240, 247], [0.5, 158, 154, 200], [1, 84, 39, 143]],
  },
  {
    key: 'rainSeason', label: 'Wet season', index: CI.rainPhase, lo: -1, hi: 1, legend: ['summer rain', 'winter rain'],
    hint: 'When the year\'s rain falls. The rain belts follow the sun north and south with the seasons, so a place they sweep across gets a wet season and a dry one. White: rain all year. Brown: the rain comes in summer (monsoons); warm, not-too-wet ground like that grows savanna. Teal: it comes in winter, with a dry summer; temperate ground like that grows Mediterranean scrub. The stronger the colour, the sharper the seasons.',
    stops: [[0, 140, 81, 10], [0.25, 216, 179, 101], [0.5, 245, 245, 245], [0.75, 90, 180, 172], [1, 1, 102, 94]],
  },
  {
    key: 'seaIce', label: 'Sea ice', index: NOT_A_COLUMN, lo: 1, hi: 2, legend: ['frozen in winter', 'frozen all year'],
    hint: 'Where the sea freezes. Pale: ice in winter that melts in summer. White: the permanent polar pack. A warm current keeps its coast open; a cold one lets the ice creep towards the equator; a bigger axial tilt freezes more of the sea in winter. Open water and land show no tint.',
    stops: [[0, 150, 190, 225], [0.5, 200, 228, 242], [1, 255, 255, 255]],
  },
];

export function climateDef(kind: ClimateKind): ClimateDef { return CLIMATE_KINDS.find((k) => k.key === kind)!; }

/** Ramp colour for `v`, written into `out` (r, g, b) as 0..255 integers. */
export function rampColor(def: Pick<ClimateDef, 'lo' | 'hi' | 'stops'>, v: number, out: number[] = [0, 0, 0]): number[] {
  const u = Math.max(0, Math.min(1, (v - def.lo) / (def.hi - def.lo)));
  const s = def.stops;
  let k = 1;
  while (k < s.length - 1 && u > s[k][0]) k++;
  const a = s[k - 1], b = s[k], f = b[0] === a[0] ? 0 : (u - a[0]) / (b[0] - a[0]);
  out[0] = Math.round(a[1] + (b[1] - a[1]) * f); out[1] = Math.round(a[2] + (b[2] - a[2]) * f); out[2] = Math.round(a[3] + (b[3] - a[3]) * f);
  return out;
}

/** CSS gradient of a ramp, for the legend bar. */
export function rampGradient(def: ClimateDef): string {
  return `linear-gradient(90deg, ${def.stops.map((s) => `rgb(${s[1]} ${s[2]} ${s[3]}) ${Math.round(s[0] * 100)}%`).join(', ')})`;
}

/** The value of one field at a tile; 0 when there is no climate (an older file, a hand-made map) or the tile is off the world. */
export function climateOf(climate: Float32Array | undefined, tile: number, field: number): number {
  if (!climate || tile < 0) return 0;
  return climate[tile * CLIMATE_STRIDE + field] ?? 0;
}

export const hasClimate = (climate: Float32Array | undefined, tile: number): boolean =>
  !!climate && tile >= 0 && (tile + 1) * CLIMATE_STRIDE <= climate.length;

/** The painted offset in kelvin for a Int8 climate-brush texel (full scale is 0.4 temperature units, ~22 K). */
export const paintedKelvin = (texel: number): number => (texel / PAINT_MAX) * CLIMATE_RANGE.temperature * KELVIN_PER_UNIT;
/** The painted rainfall offset (index units) for a Int8 texel. */
export const paintedRain = (texel: number): number => (texel / PAINT_MAX) * CLIMATE_RANGE.precipitation;

const COMPASS = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
/** The direction the air or water is moving *toward* from east/north components, as "NE 0.4", or '' when it is still. */
export function flowLabel(east: number, north: number, quiet = 0.02): string {
  const m = Math.hypot(east, north);
  if (m < quiet) return '';
  const deg = (Math.atan2(east, north) * 180) / Math.PI; // clockwise from north
  return `${COMPASS[Math.round(((deg + 360) % 360) / 45) % 8]} ${m.toFixed(2)}`;
}

/**
 * One line for the status bar: what the climate is at a tile. `paintedKelvin` / `paintedRain` are the climate
 * brush's offsets at the pixel (0 = none). `seaIce` is the engine's 0 / 1 / 2 (none, winter, all year) for the tile.
 * Empty when the document has no climate for the tile.
 */
export function describeClimate(climate: Float32Array | undefined, tile: number, paintedK = 0, paintedRain = 0, seaIce = 0): string {
  if (!hasClimate(climate, tile)) return '';
  const f = (i: number) => climateOf(climate, tile, i);
  const parts = [`temp ${f(CI.temperature).toFixed(2)}`, `rain ${f(CI.rainfall).toFixed(2)}`];
  const wind = flowLabel(f(CI.windE), f(CI.windN));
  if (wind) parts.push(`wind ${wind}`);
  const cur = flowLabel(f(CI.currentE), f(CI.currentN));
  if (cur) parts.push(`current ${cur}${f(CI.oceanHeat) > 0.01 ? ' (warm)' : f(CI.oceanHeat) < -0.01 ? ' (cold)' : ''}`);
  else if (Math.abs(f(CI.oceanHeat)) > 0.01) parts.push(`sea ${f(CI.oceanHeat) > 0 ? 'warmed' : 'cooled'} ${Math.abs(f(CI.oceanHeat) * KELVIN_PER_UNIT).toFixed(1)} K`);
  parts.push(`season ±${(f(CI.seasonalRange) * KELVIN_PER_UNIT).toFixed(0)} K`);
  if (seaIce === 1) parts.push('sea ice in winter');
  else if (seaIce === 2) parts.push('sea ice all year');
  const wet = f(CI.rainSeasonality);
  const phase = f(CI.rainPhase);
  if (wet >= 0.3) parts.push(`${wet >= 0.6 ? 'wet/dry seasons' : 'some dry season'} ${wet.toFixed(2)}${phase >= 0.3 ? ', rain in winter' : phase <= -0.3 ? ', rain in summer' : ''}`);
  if (Math.abs(paintedK) >= 0.5) parts.push(`painted ${paintedK > 0 ? '+' : ''}${paintedK.toFixed(0)} K`);
  if (Math.abs(paintedRain) >= 0.02) parts.push(`painted rain ${paintedRain > 0 ? '+' : ''}${paintedRain.toFixed(2)}`);
  return parts.join(' · ');
}
