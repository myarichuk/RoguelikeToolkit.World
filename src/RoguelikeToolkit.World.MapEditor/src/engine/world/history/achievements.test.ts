import { describe, expect, it } from 'vitest';
import { awardAchievements } from './achievements';
import { actorsOf, eventsOf, statsOf } from './actors';
import type { EventRecord, HistoryRecord, PolityRecord } from './record';

const polity = (id: number, folk: string, culture: string, founded: number, ended: number, parent = -1): PolityRecord => ({
  id, form: 'Kingdom', capital: 0, founded, ended, parent, endCause: '', folk, culture, overlay: '', name: `Realm ${id}`, style: 0,
});
const event = (id: number, kind: string, turn: number, polityId: number, tags: string[] = []): EventRecord => ({ id, kind, turn, polity: polityId, tile: 0, cause: -1, importance: 1, plugin: '', tags });

// Realm 0 and 2 are human; realm 1 is dwarven. Realm 1 falls at turn 6; realm 2 broke away from 0 at turn 3.
const record = (): HistoryRecord => ({
  turns: 10, turnYears: 10,
  polities: [polity(0, 'human', 'human_common', 0, -1), polity(1, 'dwarf', 'dwarven_clan', 0, 6), polity(2, 'human', 'human_common', 3, -1, 0)],
  events: [
    event(1, 'War', 4, 0, ['attacker:0', 'defender:1']),
    event(2, 'Conquest', 6, 0, ['winner:0', 'loser:1']),
    event(3, 'SettlementFounded', 2, 1, ['settlement:0', 'role:Colony', 'from:0']),
    event(4, 'HolyFound', 5, 2),
  ],
  // Tiles 0-3: realm 0 holds 0-2 and loses tile 2 to realm 2 at turn 3; realm 1 holds tile 3 until turn 6, when realm 0 takes it.
  ownerBase: Int32Array.of(0, 0, 0, 1),
  changes: Int32Array.of(3, 2, 2, 6, 3, 0),
});

describe('actors', () => {
  it('groups realms by people for the culture scope, one actor per realm otherwise', () => {
    expect(actorsOf(record(), 'realm').map((a) => a.name)).toEqual(['Realm 0', 'Realm 1', 'Realm 2']);
    const cultures = actorsOf(record(), 'culture');
    expect(cultures.map((a) => [a.name, a.polities])).toEqual([['Human · Human common', [0, 2]], ['Dwarf · Dwarven clan', [1]]]);
  });

  it('counts what each realm did, reading the right tag for each kind of event', () => {
    const stats = statsOf(record(), actorsOf(record(), 'realm'));
    expect(stats.get('0')).toMatchObject({ conquests: 1, wars: 1, breakaways: 1, fell: null });
    expect(stats.get('1')).toMatchObject({ settlementsFounded: 1, fell: 6 });
    expect(stats.get('2')).toMatchObject({ holySites: 1 });
  });

  it('tracks the most land held at once and when, replaying territory changes', () => {
    const stats = statsOf(record(), actorsOf(record(), 'realm'));
    expect(stats.get('0')).toMatchObject({ peakTiles: 3, peakTurn: 0 });
    expect(stats.get('1')).toMatchObject({ peakTiles: 1, peakTurn: 0 });
    // Realm 0 drops to 2 tiles at turn 3 and is back to 3 at turn 6: a tie does not move its peak turn.
    expect(stats.get('2')).toMatchObject({ peakTiles: 1, peakTurn: 3 });
  });

  it('adds up a culture across its realms', () => {
    const stats = statsOf(record(), actorsOf(record(), 'culture'));
    expect(stats.get('human|human_common')).toMatchObject({ turnsStanding: 11, conquests: 1, holySites: 1, fell: null });
    expect(stats.get('dwarf|dwarven_clan')).toMatchObject({ fell: 6 });
  });

  it('lists the events that concern a realm, including as the defender or the loser', () => {
    const dwarves = actorsOf(record(), 'realm')[1];
    expect(eventsOf(record(), dwarves).map((e) => e.id)).toEqual([1, 2, 3]);
    expect(eventsOf(record(), dwarves, (e) => e.kind === 'War').map((e) => e.id)).toEqual([1]);
  });
});

describe('achievements', () => {
  it('gives each title to the leader and says why', () => {
    const actors = actorsOf(record(), 'realm');
    const won = awardAchievements(actors, statsOf(record(), actors));
    const by = (rule: string) => won.filter((a) => a.rule === rule).map((a) => a.actor);
    expect(by('conqueror')).toEqual(['0']);
    expect(by('devout')).toEqual(['2']);
    expect(by('first-fall')).toEqual(['1']);
    expect(by('largest')).toEqual(['0']);
    expect(won.find((a) => a.rule === 'first-fall')).toMatchObject({ turn: 6, text: 'Was gone by turn 6.' });
  });

  it('awards nothing when there is only one actor to compare', () => {
    const one = actorsOf({ ...record(), polities: [record().polities[0]] }, 'realm');
    expect(awardAchievements(one, statsOf(record(), one))).toEqual([]);
  });

  it('does not hand a title to everyone when all tie', () => {
    const actors = actorsOf(record(), 'realm');
    const stats = statsOf(record(), actors);
    for (const s of stats.values()) s.turnsStanding = 5;
    expect(awardAchievements(actors, stats).some((a) => a.rule === 'enduring')).toBe(false);
  });
});
