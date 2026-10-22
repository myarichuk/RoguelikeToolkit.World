import { describe, expect, it } from 'vitest';
import { eventsByTile, placeStory, rulersOf, rulersText } from './placeHistory';
import type { EventRecord, HistoryRecord, PolityRecord } from './record';

const polity = (id: number, name: string): PolityRecord => ({ id, form: 'Kingdom', capital: 0, founded: 0, ended: -1, parent: -1, endCause: '', folk: 'human', culture: 'human_common', overlay: '', name, style: 0 });
const event = (id: number, kind: string, turn: number, polityId: number, tile: number, tags: string[] = []): EventRecord => ({ id, kind, turn, polity: polityId, tile, cause: -1, importance: 1, plugin: '', tags });

const record = (): HistoryRecord => ({
  turns: 30, turnYears: 10,
  polities: [polity(0, 'Aldmark'), polity(1, 'Brenn')],
  events: [
    event(1, 'SettlementFounded', 2, 0, 5, ['settlement:0', 'role:Colony']),
    event(2, 'SettlementTierChanged', 9, 0, 5, ['settlement:0', 'up', 'from:Village', 'to:Town']),
    event(6, 'Grudge', 10, 0, 5, ['against:1']),
    event(3, 'SettlementCaptured', 14, 1, 5, ['settlement:0', 'from:0', 'to:1']),
    event(4, 'War', 14, 0, 7, ['attacker:0', 'defender:1']),
    event(5, 'Shock', 3, -1, -1),
  ],
  ownerBase: Int32Array.of(0, 0, 0, 0, 0, 0, 0, 1),
  changes: Int32Array.of(14, 5, 1, 20, 5, 0),
});

describe('place history', () => {
  it('keeps events by their tile, skipping the ones with no place', () => {
    const index = eventsByTile(record());
    expect(index.get(5)!.map((e) => e.id)).toEqual([1, 2, 6, 3]);
    expect(index.has(-1)).toBe(false);
  });

  it('tells the story of a settlement in order, in plain words, without the realm\'s grudges', () => {
    expect(placeStory(record(), 5)).toEqual([
      { turn: 2, text: 'Aldmark founded a colony' },
      { turn: 9, text: 'a settlement of Aldmark grew into a town' },
      { turn: 14, text: 'Brenn took a settlement from Aldmark' },
    ]);
  });

  it('lists who held a tile and when, and drops unclaimed stretches', () => {
    const h = record();
    expect(rulersOf(h, 5)).toEqual([{ polity: 0, from: 0, until: 14 }, { polity: 1, from: 14, until: 20 }, { polity: 0, from: 20, until: -1 }]);
    expect(rulersOf(h, 7)).toEqual([{ polity: 1, from: 0, until: -1 }]);
    expect(rulersOf({ ...h, ownerBase: Int32Array.of(-1, -1, -1, -1, -1, -1, -1, -1), changes: new Int32Array(0) }, 5)).toEqual([]);
    expect(rulersText(rulersOf(h, 5).slice(0, 2), (id) => h.polities[id].name)).toBe('Aldmark (turn 0 to 14), then Brenn (turn 14 to 20)');
  });
});
