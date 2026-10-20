// Land capacity: what each tile could carry, as Core's LandCapacityStage computed it (five bytes per tile).
// It is derived physics, never an input, and decides nothing: it is what a plugin would rank tiles by.

export type CapacityKind = 'arable' | 'pasture' | 'game' | 'timber' | 'fish';

/** Bytes per tile in the engine's interleaved array; the order is Core's `LandCapacityInfo`. */
export const CAPACITY_STRIDE = 5;

export interface CapacityDef { key: CapacityKind; label: string; hint: string; rgb: readonly [number, number, number]; }

export const CAPACITY_KINDS: readonly CapacityDef[] = [
  { key: 'arable', label: 'Farming', rgb: [214, 168, 30], hint: 'How well the ground could be tilled: warm enough, wet enough, flat, low-lying, not salt or bare rock. A river makes a desert delta farmable.' },
  { key: 'pasture', label: 'Grazing', rgb: [112, 176, 52], hint: 'Grass for herds. Wider than farming: steppe, tundra and mountain meadow count even where nothing can be ploughed.' },
  { key: 'game', label: 'Game', rgb: [176, 98, 52], hint: 'Huntable animals: forest and scrub carry the most.' },
  { key: 'timber', label: 'Timber', rgb: [24, 120, 76], hint: 'Standing wood for building and fuel. Highest in dense forest and jungle.' },
  { key: 'fish', label: 'Fish', rgb: [32, 120, 214], hint: 'Fishing grounds on sea, lake and river tiles. Richest on shallow shelves, near coasts and where cold currents upwell (like Peru or Namibia).' },
];

export const CAPACITY_INDEX: Record<CapacityKind, number> = { arable: 0, pasture: 1, game: 2, timber: 3, fish: 4 };

/** 0..255 for one tile; 0 when the document carries no capacity (an older file, or a hand-made map). */
export function capacityOf(capacity: Uint8Array | undefined, tile: number, kind: CapacityKind): number {
  if (!capacity || tile < 0) return 0;
  return capacity[tile * CAPACITY_STRIDE + CAPACITY_INDEX[kind]] ?? 0;
}
