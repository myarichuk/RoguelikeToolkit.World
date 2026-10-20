import { describe, expect, it } from 'vitest';
import { historyPicks, historySites, settlementKind } from './historySettlements';

/** tile, polity, founded, ended, tier, role, parent, active, population */
const s = (tile: number, polity: number, tier: number, role: number, ended = -1, parent = -1) => [tile, polity, 4, ended, tier, role, parent, 1, 120];

describe('settlementKind', () => {
  it('makes a polity seat a capital whatever its size, and maps tiers to the map\'s kinds', () => {
    expect(settlementKind(1, 0)).toBe('capital');
    expect(settlementKind(3, 2)).toBe('city');
    expect(settlementKind(2, 1)).toBe('town');
    expect(settlementKind(1, 1)).toBe('village');
    expect(settlementKind(0, 1)).toBe('village');
  });
});

describe('historyPicks', () => {
  const all = () => true;

  it('turns every standing settlement into a place, with what history knows', () => {
    const picks = historyPicks({ settlements: [...s(10, 7, 3, 0), ...s(20, 7, 0, 1, -1, 0)], polityIds: [7], polityNames: ['Veldmark'] }, all);
    expect(picks.map((p) => [p.tile, p.kind])).toEqual([[10, 'capital'], [20, 'village']]);
    expect(picks[0].facts).toContainEqual(['Realm', 'Veldmark']);
    expect(picks[1].facts).toContainEqual(['Founded from', 'settlement 0']);
  });

  it('leaves ended settlements out and keeps one place per tile', () => {
    const picks = historyPicks({ settlements: [...s(10, 1, 3, 0, 12), ...s(20, 1, 2, 2), ...s(20, 2, 1, 1)] }, all);
    expect(picks.map((p) => p.tile)).toEqual([20]);
    expect(picks[0].kind).toBe('town');
  });

  it('drops settlements the map cannot hold', () => {
    const picks = historyPicks({ settlements: [...s(10, 1, 3, 0), ...s(20, 1, 2, 2)] }, (t) => t !== 10);
    expect(picks.map((p) => p.tile)).toEqual([20]);
  });

  it('names an unknown polity by its id', () => {
    const [p] = historyPicks({ settlements: s(10, 3, 2, 1) }, all);
    expect(p.facts).toContainEqual(['Realm', 'Polity 3']);
  });
});

describe('historySites', () => {
  const all = () => true;
  const base = { polityIds: [1], polityNames: ['Veldmark'] };

  it('turns an ended settlement into a ruin that says what it was and why it ended', () => {
    const picks = historySites({ ...base, settlements: s(10, 1, 2, 2, 9), endCauses: ['razed'] }, new Set(), all);
    expect(picks).toEqual([{ tile: 10, kind: 'ruin', polity: 1, facts: [['Was', 'Town of Veldmark'], ['Founded', 'turn 4'], ['Ended', 'turn 9 (being razed)']] }]);
  });

  it('adds ruin and war scars and holy sites, one mark per tile and none on a living settlement', () => {
    const picks = historySites({
      ...base, settlements: [],
      scars: [20, 1, 5, 1, 21, 1, 6, 1, 22, 1, 7, 1, 10, 1, 8, 1], scarKinds: ['ruin\tabandoned', 'battlefield\twar', 'battlefield\twar', 'crater\teruption'],
      holy: [{ tile: 21, polity: 1, turn: 3 }],
    }, new Set([22]), all);
    expect(picks.map((p) => [p.tile, p.kind])).toEqual([[20, 'ruin'], [21, 'shrine']]);
  });

  it('keeps a battlefield where nothing else stands, and skips tiles the map cannot hold', () => {
    const picks = historySites({ ...base, settlements: [], scars: [30, 1, 5, 1, 31, 1, 6, 1], scarKinds: ['battlefield\twar', 'battlefield\twar'] }, new Set(), (t) => t !== 31);
    expect(picks.map((p) => [p.tile, p.kind])).toEqual([[30, 'battlefield']]);
  });
});
