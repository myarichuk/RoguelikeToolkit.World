// Procedural hand-inked fantasy-map stamps (classic sepia ink-map style).
//
// Every generator is deterministic: (id, seed, params) -> byte-identical Stamp.
// Authoring scale: UNITS_PER_MAP_UNIT box units per map unit at `defaultSize`, i.e. a
// stamp's nominal visual height is about defaultSize * UNITS_PER_MAP_UNIT box units, so
// ink line weights match across generators when each is placed at its defaultSize.
// To place a stamp at `size` map units: scale = size / (defaultSize * UNITS_PER_MAP_UNIT)
// (see `stampScale`), then put `anchor` on the map point.

import { Rng, mixSeed } from '../ink';
import type { PaletteToken, Stamp } from '../ink';
import { resolveParams } from './common';
import type { Params, ParamSpec } from './common';
import { genMountain, genVolcano, genHill } from './mountains';
import { genConifer, genDeciduous, genPalm, genDeadTree, genShrub, genGrassTuft, genReeds, genCactus, genDunes, genRock, genCrevasse } from './vegetation';
import { genVillage, genTown, walledCity, genCastle, genTower, genRuins, genTemple, genPort, genLighthouse } from './architecture';
import { genWave, genShip, genSeaMonster, genWhirlpool } from './water';
import { genCompass } from './decoration';

export type { PaletteToken, StampShape, Stamp } from '../ink';

export type StampCategory = 'mountain' | 'hill' | 'tree' | 'vegetation' | 'settlement' | 'structure' | 'water' | 'terrain' | 'decoration';

export interface GeneratorInfo {
  id: string;
  label: string;
  category: StampCategory;
  /** Map units, ~ visual height on a 2048x1024 world map. */
  defaultSize: number;
  params?: Record<string, { min: number; max: number; default: number }>;
  generate(seed: number, params?: Record<string, number>): Stamp;
}

export const UNITS_PER_MAP_UNIT = 5;

export const DEFAULT_PALETTE: Record<PaletteToken, string> = {
  ink: '#2b2119',
  paper: '#e9dcbc',
  shade: '#c7b28a',
  light: '#f5ecd4',
  foliage: '#b3b07c',
  foliageDark: '#8c8a5c',
  water: '#b9c2ad',
  roof: '#b0694a',
  wall: '#efe3c6',
  snow: '#f8f3e6',
  sand: '#dcc58f',
  none: 'none',
};

// line weights (box units)

// ================================================================ registry

type GenFn = (r: Rng, p: Params) => Stamp;

function def(id: string, label: string, category: StampCategory, defaultSize: number, fn: GenFn, params?: ParamSpec, fixed?: Params): GeneratorInfo {
  const info: GeneratorInfo = {
    id, label, category, defaultSize,
    generate(seed: number, given?: Params): Stamp {
      const p = resolveParams(params, given);
      if (fixed) for (const k of Object.keys(fixed)) if (given?.[k] === undefined) p[k] = fixed[k]!;
      return fn(new Rng(mixSeed(id, seed >>> 0)), p);
    },
  };
  if (params) info.params = params;
  return info;
}

const MOUNTAIN_PARAMS: ParamSpec = {
  peaks: { min: 0, max: 3, default: 0 }, // 0 = pick from the seed
  snow: { min: 0, max: 1, default: 0 },
  height: { min: 0.6, max: 1.4, default: 1 },
  steep: { min: 0.7, max: 1.5, default: 1 },
};

const LIST: GeneratorInfo[] = [
  def('mountain', 'Mountain', 'mountain', 18, genMountain, MOUNTAIN_PARAMS),
  def('mountainSnow', 'Snowy mountain', 'mountain', 18, genMountain, MOUNTAIN_PARAMS, { snow: 1 }),
  def('volcano', 'Volcano', 'mountain', 18, genVolcano, { smoke: { min: 0, max: 1, default: 0.6 } }),
  def('hill', 'Hill', 'hill', 5, genHill, { width: { min: 0.7, max: 1.4, default: 1 } }),
  def('conifer', 'Conifer', 'tree', 6, genConifer),
  def('deciduous', 'Deciduous tree', 'tree', 6, genDeciduous),
  def('palm', 'Palm', 'tree', 6, genPalm),
  def('deadTree', 'Dead tree', 'tree', 5, genDeadTree),
  def('shrub', 'Shrub', 'vegetation', 2.5, genShrub),
  def('grassTuft', 'Grass tuft', 'vegetation', 2, genGrassTuft),
  def('reeds', 'Reeds (swamp)', 'vegetation', 4, genReeds),
  def('cactus', 'Cactus', 'vegetation', 5, genCactus),
  def('dunes', 'Dunes', 'terrain', 4, genDunes),
  def('rock', 'Rocks', 'terrain', 3, genRock),
  def('glacierCrack', 'Glacier crevasses', 'terrain', 4, genCrevasse),
  def('village', 'Village', 'settlement', 8, genVillage, { houses: { min: 0, max: 5, default: 0 } }),
  def('town', 'Town', 'settlement', 11, genTown),
  def('city', 'City', 'settlement', 16, (r) => walledCity(r, false)),
  def('capital', 'Capital', 'settlement', 22, (r) => walledCity(r, true)),
  def('castle', 'Castle', 'structure', 11, genCastle),
  def('tower', 'Tower', 'structure', 8, genTower),
  def('ruins', 'Ruins', 'structure', 5, genRuins),
  def('temple', 'Temple', 'structure', 8, genTemple),
  def('port', 'Port', 'settlement', 8, genPort),
  def('lighthouse', 'Lighthouse', 'structure', 9, genLighthouse),
  def('wave', 'Wave', 'water', 2, genWave),
  def('ship', 'Ship', 'water', 8, genShip),
  def('seaMonster', 'Sea monster', 'water', 6, genSeaMonster),
  def('whirlpool', 'Whirlpool', 'water', 5, genWhirlpool),
  def('compass', 'Compass rose', 'decoration', 60, genCompass, { points: { min: 0, max: 32, default: 0 } }),
];

export const GENERATORS: Record<string, GeneratorInfo> = Object.fromEntries(LIST.map((g) => [g.id, g]));

export function makeStamp(id: string, seed: number, params?: Record<string, number>): Stamp {
  const g = GENERATORS[id];
  if (!g) throw new Error(`Unknown stamp generator: ${id}`);
  return g.generate(seed, params);
}

/** Map units per box unit when the stamp is drawn at `size` map units (default: its defaultSize). */
export function stampScale(id: string, size?: number): number {
  const g = GENERATORS[id];
  if (!g) throw new Error(`Unknown stamp generator: ${id}`);
  return (size ?? g.defaultSize) / (g.defaultSize * UNITS_PER_MAP_UNIT);
}

/** Inner SVG markup (paths in box coordinates), no <svg> wrapper. */
export function stampToSvg(stamp: Stamp, palette: Record<PaletteToken, string> = DEFAULT_PALETTE): string {
  let out = '<g stroke-linecap="round" stroke-linejoin="round">';
  for (const s of stamp.shapes) {
    let a = `<path d="${s.d}" fill="${palette[s.fill] ?? 'none'}"`;
    if (s.stroke !== 'none') a += ` stroke="${palette[s.stroke]}" stroke-width="${s.width ?? 1}"`;
    if (s.opacity !== undefined) a += ` opacity="${s.opacity}"`;
    out += a + '/>';
  }
  return out + '</g>';
}
