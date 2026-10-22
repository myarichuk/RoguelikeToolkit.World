import { describe, expect, it } from 'vitest';
import { realmNameRequest, realmTitle } from './realmNames';

describe('realm names', () => {
  it('draws from the land a form would name itself after, not always a city', () => {
    const kinds = new Set(Array.from({ length: 40 }, (_, id) => realmNameRequest(1, id, 'Horde', 'orc', 'fantasy_common').kind));
    expect([...kinds].every((k) => ['mountain', 'hold', 'valley'].includes(k))).toBe(true);
    expect(kinds.size).toBeGreaterThan(1);
  });

  it('is deterministic for a seed and a realm, and differs between realms', () => {
    expect(realmNameRequest(7, 3, 'Kingdom', 'human', 'human_welsh')).toEqual(realmNameRequest(7, 3, 'Kingdom', 'human', 'human_welsh'));
    expect(realmNameRequest(7, 3, 'Kingdom', 'human', 'human_welsh').seed).not.toBe(realmNameRequest(7, 4, 'Kingdom', 'human', 'human_welsh').seed);
  });

  it('titles a world of one form in more than one way', () => {
    const titles = new Set(Array.from({ length: 30 }, (_, id) => realmTitle(5, id, 'Kingdom', 'Eldershaw')));
    expect(titles.size).toBeGreaterThan(2);
    expect([...titles].every((t) => t.includes('Eldershaw'))).toBe(true);
  });

  it('falls back to the bare name for a form it does not know', () => {
    expect(realmTitle(1, 0, 'Theocracy', 'Aram')).toBe('Aram');
  });
});
