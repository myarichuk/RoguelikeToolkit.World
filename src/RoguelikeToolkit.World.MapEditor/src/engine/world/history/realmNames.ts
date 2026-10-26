import { hash2 } from '../../core/rng';
import type { NameRequest } from '../protocol';

/** How one people names realms of one form: the place kinds a base name is drawn from, and title templates (`{n}`). */
export interface RealmNaming { kinds: readonly string[]; titles: readonly string[] }

/** Used when the name data has nothing for a people and form: a valley or hold name, untitled. */
export const FALLBACK_NAMING: RealmNaming = { kinds: ['valley', 'hold'], titles: ['{n}'] };

/** The engine's `RealmNaming` export (element 0: tab-joined kinds; the rest: titles) as a `RealmNaming`. */
export function parseRealmNaming(raw: readonly string[]): RealmNaming {
  const kinds = (raw[0] ?? '').split('\t').filter(Boolean), titles = raw.slice(1).filter((t) => t.includes('{n}'));
  return { kinds: kinds.length ? kinds : FALLBACK_NAMING.kinds, titles: titles.length ? titles : FALLBACK_NAMING.titles };
}

/**
 * The place name a realm is built on, asked of the naming style of its people. Realms are named from the land, not the
 * town ("Goldhaven" for every realm): the name data lists, per people and form, the kinds of place that name a realm.
 */
export function realmNameRequest(seed: number, polity: number, naming: RealmNaming, race: string, culture: string): NameRequest {
  const kinds = naming.kinds.length ? naming.kinds : FALLBACK_NAMING.kinds;
  const h = hash2(seed, 0x7e11 + polity);
  return { kind: kinds[h % kinds.length], seed: h & 0x7fffffff, race, culture, context: 0 };
}

/** The realm's full title around its base name, from the people's title templates. */
export function realmTitle(seed: number, polity: number, naming: RealmNaming, name: string): string {
  const titles = naming.titles.length ? naming.titles : FALLBACK_NAMING.titles;
  return titles[hash2(seed, 0x7171 + polity) % titles.length].replace('{n}', name);
}
