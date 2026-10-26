import { describe, expect, it } from 'vitest';
import { BATTLE_STRIDE, battleOfEvent, battlesAt, battlesOfPerson, battlesOfPolity, decodeBattles, OUTCOME_COLORS, OUTCOME_LABELS, wonBy } from './battles';
import { event, expansion, STRINGS } from './expansionFixture';

const OUTCOMES = ['AttackerDecisive', 'AttackerVictory', 'AttackerPyrrhic', 'Stalemate', 'DefenderVictory', 'DefenderDecisive', 'Averted'];

/** id, turn, year, season, kind, tile, settlement, outcome, parent, event, months, end; then attacker and defender sides. */
function battle(id: number, turn: number, kind: number, outcome: number, a: [number, number], d: [number, number], siege: [number, number, number] = [-1, 0, -1]): number[] {
  return [id, turn, 3, 3, kind, 40 + id, siege[0], outcome, -1, 100 + id, siege[1], siege[2], a[0], a[1], 900, 250, 800, 600, d[0], d[1], 700, 500, 1000, 400];
}

const record = () => expansion({
  battles: Int32Array.from([
    ...battle(0, 2, 0, 0, [0, 1], [1, 3]),
    ...battle(1, 2, 2, 1, [0, 1], [1, -1], [5, 9, STRINGS.indexOf('stormed')]),
    ...battle(2, 4, 0, 5, [1, 3], [0, -1]),
  ]),
  battleText: ['forest\tshieldwall\tspearmen\tambush\tarchers\tambush,rout', 'open\tsiege_engines\tsappers\t\t\t', 'hills\t\t\t\t\t'],
});

describe('battles', () => {
  it('decodes the engine stride, shares scaled back to 0..1', () => {
    const [field, siege] = decodeBattles(record());
    expect(BATTLE_STRIDE).toBe(24);
    expect(field).toMatchObject({ id: 0, turn: 2, season: 'Winter', kind: 'Field', outcome: 'AttackerDecisive', event: 100, terrain: 'forest', beats: ['ambush', 'rout'], siegeEnd: '' });
    expect(field.attacker).toEqual({ polity: 0, general: 1, strength: 900, loss: 0.25, supply: 0.8, morale: 0.6, tactic: 'shieldwall', unit: 'spearmen' });
    expect(field.defender).toMatchObject({ polity: 1, general: 3, tactic: 'ambush', unit: 'archers', morale: 0.4 });
    expect(siege).toMatchObject({ kind: 'Siege', settlement: 5, siegeMonths: 9, siegeEnd: 'stormed', beats: [] });
  });

  it('finds the battles of a person, a realm and a turn', () => {
    const all = decodeBattles(record());
    expect(battlesOfPerson(all, 1).map((b) => b.id)).toEqual([0, 1]);
    expect(battlesOfPerson(all, 3).map((b) => b.id)).toEqual([0, 2]);
    expect(battlesOfPolity(all, 1).map((b) => b.id)).toEqual([0, 1, 2]);
    expect(battlesAt(all, 2).map((b) => b.id)).toEqual([0, 1]);
    expect(battlesAt(all, 3)).toEqual([]);
  });

  it('links an event to its battle record by the battle: tag', () => {
    const all = decodeBattles(record());
    expect(battleOfEvent(all, event(100, 'War', 2, ['war', 'battle:2']))?.id).toBe(2);
    expect(battleOfEvent(all, event(101, 'War', 2, ['war']))).toBeUndefined();
  });

  it('says whether a commander won, from either side', () => {
    const [field, , rout] = decodeBattles(record());
    expect(wonBy(field, 1)).toBe(true);
    expect(wonBy(field, 3)).toBe(false);
    expect(wonBy(rout, 3)).toBe(false);
    expect(wonBy({ ...field, outcome: 'Stalemate' }, 1)).toBeNull();
  });

  it('has a colour and a label for every outcome the engine names', () => {
    for (const o of OUTCOMES) { expect(OUTCOME_COLORS[o]).toMatch(/^#/); expect(OUTCOME_LABELS[o]).toBeTruthy(); }
  });
});
