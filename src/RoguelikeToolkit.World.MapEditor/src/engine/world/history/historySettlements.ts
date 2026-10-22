import type { FeatureKind } from '../protocol';
import { since, type Span } from './timeline';

/** Ints per settlement in the engine's `HistorySettlements()`: tile, polity, founded, ended (-1 standing), tier, role, parent, active, population. */
export const SETTLEMENT_STRIDE = 9;

/** What history says about the world's inhabited places, as the engine exports it. */
export interface HistoryInput {
  /** Flat `HistorySettlements()`. */
  settlements: ArrayLike<number>;
  /** Display name per polity id (the index is the polity id's position in `HistoryPolities()`; see `polityIndex`). */
  polityNames?: ArrayLike<string>;
  /** Polity id per `polityNames` entry. */
  polityIds?: ArrayLike<number>;
  /** Founding folk key per `polityIds` entry (`human`, `dwarf`, ...). */
  polityFolk?: ArrayLike<string>;
  /** Name style (index into the chosen styles) each `polityIds` entry renders its places through, -1 when it has none. */
  polityStyle?: ArrayLike<number>;
  /** Why each settlement ended, aligned with `settlements`; empty while it stands. */
  endCauses?: ArrayLike<string>;
  /** Flat `HistoryScars()`: tile, polity, founded turn, active. */
  scars?: ArrayLike<number>;
  /** Per scar: `kind\tcause`. */
  scarKinds?: ArrayLike<string>;
  /** Holy sites: where a polity founded one, and when. */
  holy?: { tile: number; polity: number; turn: number }[];
}

export interface HistoryPick {
  tile: number;
  kind: FeatureKind;
  /** The realm it belongs to (-1 none); it names the place in its folk's tongue. */
  polity: number;
  /** Inspector facts specific to history, already formatted. */
  facts: [string, string][];
  /** The turns it can be seen on the map. A ruin appears when its settlement falls: before that, only the living town was drawn. */
  span: Span;
}

const TIER = ['Hamlet', 'Village', 'Town', 'City'];
const ROLE = ['Capital', 'Colony', 'Trade node', 'Garrison post', 'Shrine town', 'Refuge'];

/** History's tier and role as the map's settlement kind: a polity seat is a capital, whatever its size; a hamlet draws as a village. */
export function settlementKind(tier: number, role: number): FeatureKind {
  if (role === 0) return 'capital';
  return tier >= 3 ? 'city' : tier === 2 ? 'town' : 'village';
}

/**
 * The map's settlements when history is the source of truth: every standing settlement of the
 * catalog, on its own tile, with what history knows about it. Ended ones (razed, abandoned) are
 * not drawn as places; `allowed` filters tiles that cannot hold one on this map (drowned,
 * off-map, inside a drawn lake). A tile holds one settlement: the first standing one wins.
 */
export function historyPicks(h: HistoryInput, allowed: (tile: number) => boolean): HistoryPick[] {
  const n = Math.floor(h.settlements.length / SETTLEMENT_STRIDE);
  const names = new Map<number, string>();
  if (h.polityNames && h.polityIds) for (let i = 0; i < h.polityIds.length; i++) names.set(h.polityIds[i], h.polityNames[i]);
  const taken = new Set<number>();
  const out: HistoryPick[] = [];
  for (let i = 0; i < n; i++) {
    const o = i * SETTLEMENT_STRIDE;
    const tile = h.settlements[o], polity = h.settlements[o + 1], founded = h.settlements[o + 2], ended = h.settlements[o + 3];
    const tier = h.settlements[o + 4], role = h.settlements[o + 5], parent = h.settlements[o + 6], pop = h.settlements[o + 8];
    if (ended >= 0 || taken.has(tile) || !allowed(tile)) continue;
    taken.add(tile);
    const facts: [string, string][] = [
      ['Role', ROLE[role] ?? 'Settlement'],
      ['Size', TIER[tier] ?? 'Settlement'],
      ['Realm', names.get(polity) ?? (polity >= 0 ? `Polity ${polity}` : 'None')],
      ['Founded', `turn ${founded}`],
      ['Population', String(pop)],
    ];
    if (parent >= 0 && parent < n) facts.push(['Founded from', `settlement ${parent}`]);
    out.push({ tile, kind: settlementKind(tier, role), polity, facts, span: since(founded) });
  }
  return out;
}

/** Ints per scar in the engine's `HistoryScars()`. */
export const SCAR_STRIDE = 4;

const SCAR_CAUSE: Record<string, string> = {
  war: 'a war', plague: 'plague', famine: 'famine', drought: 'drought', quake: 'an earthquake', flood: 'a flood', eruption: 'an eruption',
  abandoned: 'abandonment', razed: 'being razed',
};
const cause = (k: string) => SCAR_CAUSE[k] ?? (k || 'unknown causes');

/**
 * What history left standing or fallen that is not a living settlement: ruins (an ended
 * settlement, or a ruin scar), battlefields (war scars) and holy sites. `taken` holds tiles
 * already used (living settlements); a tile gets one mark, ruins before holy sites before battlefields.
 */
export function historySites(h: HistoryInput, taken: ReadonlySet<number>, allowed: (tile: number) => boolean): HistoryPick[] {
  const names = new Map<number, string>();
  if (h.polityNames && h.polityIds) for (let i = 0; i < h.polityIds.length; i++) names.set(h.polityIds[i], h.polityNames[i]);
  const realm = (p: number) => names.get(p) ?? (p >= 0 ? `Polity ${p}` : 'None');
  const used = new Set(taken);
  const out: HistoryPick[] = [];
  const add = (tile: number, kind: FeatureKind, polity: number, from: number, facts: [string, string][]) => {
    if (used.has(tile) || !allowed(tile)) return;
    used.add(tile);
    out.push({ tile, kind, polity, facts, span: since(from) });
  };
  const n = Math.floor(h.settlements.length / SETTLEMENT_STRIDE);
  for (let i = 0; i < n; i++) {
    const o = i * SETTLEMENT_STRIDE;
    if (h.settlements[o + 3] < 0) continue;
    const end = h.endCauses?.[i] ?? '';
    add(h.settlements[o], 'ruin', h.settlements[o + 1], h.settlements[o + 3], [
      ['Was', `${TIER[h.settlements[o + 4]] ?? 'Settlement'} of ${realm(h.settlements[o + 1])}`],
      ['Founded', `turn ${h.settlements[o + 2]}`], ['Ended', `turn ${h.settlements[o + 3]}${end ? ` (${cause(end)})` : ''}`],
    ]);
  }
  const scars = Math.floor((h.scars?.length ?? 0) / SCAR_STRIDE);
  const scar = (i: number) => {
    const [kind, why] = (h.scarKinds?.[i] ?? '').split('\t');
    return { tile: h.scars![i * SCAR_STRIDE], polity: h.scars![i * SCAR_STRIDE + 1], turn: h.scars![i * SCAR_STRIDE + 2], kind, why };
  };
  for (let i = 0; i < scars; i++) {
    const s = scar(i);
    if (s.kind === 'ruin') add(s.tile, 'ruin', s.polity, s.turn, [['Was', `settlement of ${realm(s.polity)}`], ['Ended', `turn ${s.turn} (${cause(s.why)})`]]);
  }
  for (const s of h.holy ?? []) add(s.tile, 'shrine', s.polity, s.turn, [['Founded by', realm(s.polity)], ['Founded', `turn ${s.turn}`]]);
  for (let i = 0; i < scars; i++) {
    const s = scar(i);
    if (s.kind === 'battlefield') add(s.tile, 'battlefield', s.polity, s.turn, [['Fought on land of', realm(s.polity)], ['Fought', `turn ${s.turn}`]]);
  }
  return out;
}
