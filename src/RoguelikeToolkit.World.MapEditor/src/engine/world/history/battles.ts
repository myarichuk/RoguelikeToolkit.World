import { tagNumber, type EventRecord, type ExpansionRecord } from './record';

/** Ints per battle in the engine's `HistoryBattles()` (WorldExports.BattleStride). */
export const BATTLE_STRIDE = 24;

/** `HistorySeason` by value; the export carries the number. */
export const SEASONS = ['Spring', 'Summer', 'Autumn', 'Winter'] as const;

/** One side of an engagement. */
export interface BattleSide {
  polity: number;
  /** Commanding person, -1 none. */
  general: number;
  strength: number;
  /** Shares 0..1. */
  loss: number;
  supply: number;
  morale: number;
  tactic: string;
  unit: string;
}

/** A battle record: a field battle, an ambush, a siege, a relief battle or a naval clash. */
export interface Battle {
  id: number;
  turn: number;
  year: number;
  season: string;
  /** `BattleKind` name: Field, Ambush, Siege, Relief, Naval. */
  kind: string;
  /** Where it was fought (for a siege, the field the besiegers came from). */
  tile: number;
  /** The besieged settlement's index, -1 unless a siege. */
  settlement: number;
  /** `BattleOutcome` name, from the attacker's side. */
  outcome: string;
  /** The battle this one follows (a siege its field battle, a relief battle its siege), -1 none. */
  parent: number;
  /** The event that narrates it, -1 none (compacted). */
  event: number;
  siegeMonths: number;
  /** How a siege ended (`stormed`, `surrendered`, `lifted`...), empty otherwise. */
  siegeEnd: string;
  terrain: string;
  beats: string[];
  attacker: BattleSide;
  defender: BattleSide;
}

export function decodeBattles(x: ExpansionRecord): Battle[] {
  const b = x.battles, n = Math.floor(b.length / BATTLE_STRIDE), out: Battle[] = [];
  const str = (id: number) => (id >= 0 ? x.strings[id] ?? '' : '');
  for (let i = 0; i < n; i++) {
    const o = i * BATTLE_STRIDE;
    const [terrain = '', aTactic = '', aUnit = '', dTactic = '', dUnit = '', beats = ''] = (x.battleText[i] ?? '').split('\t');
    const side = (s: number, tactic: string, unit: string): BattleSide => ({
      polity: b[s], general: b[s + 1], strength: b[s + 2], loss: b[s + 3] / 1000, supply: b[s + 4] / 1000, morale: b[s + 5] / 1000, tactic, unit,
    });
    out.push({
      id: b[o], turn: b[o + 1], year: b[o + 2], season: SEASONS[b[o + 3]] ?? '', kind: x.battleKinds[b[o + 4]] ?? '', tile: b[o + 5], settlement: b[o + 6],
      outcome: x.battleOutcomes[b[o + 7]] ?? '', parent: b[o + 8], event: b[o + 9], siegeMonths: b[o + 10], siegeEnd: str(b[o + 11]),
      terrain, beats: beats.split(',').filter(Boolean), attacker: side(o + 12, aTactic, aUnit), defender: side(o + 18, dTactic, dUnit),
    });
  }
  return out;
}

/** The battles a person commanded, on either side, oldest first. */
export const battlesOfPerson = (battles: readonly Battle[], person: number): Battle[] =>
  battles.filter((b) => b.attacker.general === person || b.defender.general === person);

/** The battles a realm fought, on either side. */
export const battlesOfPolity = (battles: readonly Battle[], polity: number): Battle[] =>
  battles.filter((b) => b.attacker.polity === polity || b.defender.polity === polity);

/** The battles fought in one turn. */
export const battlesAt = (battles: readonly Battle[], turn: number): Battle[] => battles.filter((b) => b.turn === turn);

/** The battle an event narrates (its `battle:{id}` tag), if any. */
export const battleOfEvent = (battles: readonly Battle[], e: EventRecord): Battle | undefined => {
  const id = tagNumber(e, 'battle');
  return id === undefined ? undefined : battles.find((b) => b.id === id);
};

/** Whether the person's side won: true, false, or null for a stalemate or a battle never fought. */
export function wonBy(b: Battle, person: number): boolean | null {
  const attacker = b.attacker.general === person;
  if (b.outcome.startsWith('Attacker')) return attacker;
  if (b.outcome.startsWith('Defender')) return !attacker;
  return null;
}

/**
 * Marker colour per outcome (`BattleOutcome` name): attackers' wins warm, defenders' wins cool, stalemates and averted
 * campaigns grey. A symbol of the result, not of a people, so it lives here and not in the content pack.
 */
export const OUTCOME_COLORS: Record<string, string> = {
  AttackerDecisive: '#b3261e', AttackerVictory: '#e0662a', AttackerPyrrhic: '#d9a52b',
  Stalemate: '#8a8a8a', DefenderVictory: '#3c7fc0', DefenderDecisive: '#1f4f9a', Averted: '#c9c3b6',
};

export const OUTCOME_LABELS: Record<string, string> = {
  AttackerDecisive: 'Attackers routed the defenders', AttackerVictory: 'Attackers won', AttackerPyrrhic: 'Attackers won at a ruinous cost',
  Stalemate: 'Stalemate', DefenderVictory: 'Defenders held', DefenderDecisive: 'Defenders routed the attackers', Averted: 'No battle (averted)',
};

export const outcomeColor = (outcome: string): string => OUTCOME_COLORS[outcome] ?? '#8a8a8a';
