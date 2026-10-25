import { describe, expect, it } from 'vitest';
import { FALLBACK_NAMING, parseRealmNaming, realmNameRequest, realmTitle } from './realmNames';

const horde = parseRealmNaming(['mountain\thold\tvalley', '{n} Horde', 'Host of {n}']);
const kingdom = parseRealmNaming(['valley\tforest', 'Kingdom of {n}', 'Realm of {n}', '{n} Crown', 'Principality of {n}']);

describe('realm names', () => {
  it('draws from the land the name data lists for a form, not always a city', () => {
    const kinds = new Set(Array.from({ length: 40 }, (_, id) => realmNameRequest(1, id, horde, 'orc', 'fantasy_common').kind));
    expect([...kinds].every((k) => ['mountain', 'hold', 'valley'].includes(k))).toBe(true);
    expect(kinds.size).toBeGreaterThan(1);
  });

  it('is deterministic for a seed and a realm, and differs between realms', () => {
    expect(realmNameRequest(7, 3, kingdom, 'human', 'human_welsh')).toEqual(realmNameRequest(7, 3, kingdom, 'human', 'human_welsh'));
    expect(realmNameRequest(7, 3, kingdom, 'human', 'human_welsh').seed).not.toBe(realmNameRequest(7, 4, kingdom, 'human', 'human_welsh').seed);
  });

  it('titles a world of one form in more than one way', () => {
    const titles = new Set(Array.from({ length: 30 }, (_, id) => realmTitle(5, id, kingdom, 'Eldershaw')));
    expect(titles.size).toBeGreaterThan(2);
    expect([...titles].every((t) => t.includes('Eldershaw'))).toBe(true);
  });

  it('falls back to a bare name when the data has nothing for the form', () => {
    expect(parseRealmNaming([''])).toEqual(FALLBACK_NAMING);
    expect(realmTitle(1, 0, parseRealmNaming(['']), 'Aram')).toBe('Aram');
  });

  it('drops templates without a place for the name', () => {
    expect(parseRealmNaming(['valley', 'Broken Title', '{n} Moot']).titles).toEqual(['{n} Moot']);
  });
});
