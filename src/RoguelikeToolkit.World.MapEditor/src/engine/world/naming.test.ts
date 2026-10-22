import { describe, expect, it } from 'vitest';
import { polityStyleIndex, tileNamingStyles } from './naming';

// A line of 6 tiles: 0-1-2 land, 3 water, 4-5 land.
const adj = { start: Int32Array.from([0, 1, 3, 5, 7, 9, 10]), list: Int32Array.from([1, 0, 2, 1, 3, 2, 4, 3, 5, 4]) };
const base = { seed: 7, n: 6, adj, isLand: (t: number) => t !== 3, styleCount: 3 };

describe('tileNamingStyles', () => {
  it('names owned tiles by their realm and spreads over land then water', () => {
    const s = tileNamingStyles({ ...base, owner: [5, 5, -1, -1, -1, -1], polityStyle: new Map([[5, 2]]) });
    expect([s[0], s[1], s[2]]).toEqual([2, 2, 2]);
    expect(s[3]).toBe(2); // water takes its nearest land
  });
  it('gives a landmass no realm reaches one style of its own', () => {
    const s = tileNamingStyles({ ...base, owner: [5, 5, 5, -1, -1, -1], polityStyle: new Map([[5, 1]]) });
    expect(s[4]).toBe(s[5]);
    expect(s[4]).toBeLessThan(3);
  });
  it('still names every tile when history is off', () => {
    const s = tileNamingStyles({ ...base, owner: [], polityStyle: new Map() });
    expect(s[0]).toBe(s[2]);
    expect([...s].every((v) => v < 3)).toBe(true);
  });
});

describe('polityStyleIndex', () => {
  const styles = [
    { race: 'human', culture: 'human_western', label: 'W' }, { race: 'human', culture: 'human_welsh', label: 'Wl' },
    { race: 'orc', culture: 'fantasy_common', label: 'O' },
  ];
  it('prefers the exact race and culture over any style of the race', () => {
    expect(polityStyleIndex(styles, 'human', 'human_welsh', 'human', 1, 0)).toBe(1);
  });
  it('falls back to a style of the folk, then to none', () => {
    expect(polityStyleIndex(styles, '', '', 'orc', 1, 0)).toBe(2);
    expect(polityStyleIndex(styles, '', '', 'dwarf', 1, 0)).toBeUndefined();
  });
});
