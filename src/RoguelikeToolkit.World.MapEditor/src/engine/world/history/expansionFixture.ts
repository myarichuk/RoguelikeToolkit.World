// Test fixture: a small expansion record in the engine's layout (persons, bonds, offices; battles and routes per test).
import { PERSON_STRIDE } from './persons';
import type { EventRecord, ExpansionRecord, HistoryRecord } from './record';

const KINDS = ['Ruler', 'Heir', 'Advisor', 'General', 'Spymaster', 'MerchantPrince', 'RebelLeader', 'Outlaw', 'Privateer', 'Prophet', 'Agent'];
export const STRINGS = ['age', 'ruler', 'marshal', 'patron', 'agent_of', 'sponsor', 'spymaster', 'battle', 'stormed'];
const s = (name: string) => STRINGS.indexOf(name);

/** One person row: the fields a test sets, zeros elsewhere. */
function row(id: number, kind: string, polity: number, born: number, died: number, extra: Partial<Record<number, number>> = {}): number[] {
  const r = new Array<number>(PERSON_STRIDE).fill(0);
  r[0] = id; r[1] = KINDS.indexOf(kind); r[2] = polity; r[3] = -1; r[5] = -1; r[6] = -1; r[7] = born; r[9] = died; r[10] = died >= 0 ? s('age') : -1;
  for (const [k, v] of Object.entries(extra)) r[Number(k)] = v!;
  return r;
}

export function expansion(over: Partial<ExpansionRecord> = {}): ExpansionRecord {
  const persons = [row(0, 'Ruler', 0, 0, 6), row(1, 'General', 0, 2, -1, { 4: 1, 12: 12345 }), row(2, 'Agent', 1, 3, -1), row(3, 'Spymaster', 1, 1, -1), row(4, 'Outlaw', -1, 4, -1), row(5, 'Privateer', 1, 4, -1)];
  const relations = [[0, 1, s('patron'), 2, -1, 1], [2, 3, s('agent_of'), 3, -1, 1], [3, 5, s('sponsor'), 4, -1, 1]];
  const tenures = [[0, 0, s('ruler'), 0, 6], [1, 0, s('marshal'), 2, -1], [1, 0, s('ruler'), 6, -1]];
  return {
    strings: STRINGS, personKinds: KINDS, battleKinds: ['Field', 'Ambush', 'Siege', 'Relief', 'Naval'],
    battleOutcomes: ['AttackerDecisive', 'AttackerVictory', 'AttackerPyrrhic', 'Stalemate', 'DefenderVictory', 'DefenderDecisive', 'Averted'],
    persons: Int32Array.from(persons.flat()), personText: persons.map((_, i) => `human\thuman_common\t${i === 0 ? 'cruel,cunning' : ''}`),
    personNames: persons.map((_, i) => (i === 3 ? '' : `Name ${i}`)),
    relations: Int32Array.from(relations.flat()), tenures: Int32Array.from(tenures.flat()),
    battles: new Int32Array(0), battleText: [], routeStates: new Int32Array(0), routeSeries: new Int32Array(0), roads: new Int32Array(0), settlementTiles: new Int32Array(0),
    ...over,
  };
}

export const event = (id: number, kind: string, turn: number, tags: string[], parts?: EventRecord['parts']): EventRecord =>
  ({ id, kind, turn, polity: 0, tile: 1, cause: -1, importance: 1, plugin: '', tags, ...(parts ? { parts } : {}) });

export function history(events: EventRecord[] = [], x = expansion()): HistoryRecord {
  return { turns: 10, turnYears: 10, polities: [], events, ownerBase: new Int32Array(0), changes: new Int32Array(0), expansion: x };
}
