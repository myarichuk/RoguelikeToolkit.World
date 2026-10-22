import { hash2 } from '../../core/rng';
import type { NameRequest } from '../protocol';

/**
 * Realms are named from the land, not the town: a city roll gives "Goldhaven, Greyhaven, Greyburg" for every realm,
 * while valleys, forests, mountains and seas each bring their own endings (-dale, -holt, -fell, -strait). Each form
 * leans on the places its people would take their name from.
 */
const NAME_KINDS: Record<string, readonly string[]> = {
  Kingdom: ['valley', 'forest', 'hold'],
  MerchantRepublic: ['sea', 'lake', 'river'],
  LooseAssociation: ['river', 'valley', 'forest'],
  Horde: ['mountain', 'hold', 'valley'],
};

/** How a form's realm is titled; `{n}` is the name. Several per form, so a world of kingdoms is not all "Kingdom of". */
const TITLES: Record<string, readonly string[]> = {
  Kingdom: ['Kingdom of {n}', 'Realm of {n}', '{n} Crown', 'Principality of {n}'],
  MerchantRepublic: ['{n} Republic', 'Free Ports of {n}', '{n} Compact', 'Commonwealth of {n}'],
  LooseAssociation: ['{n} League', '{n} Confederacy', '{n} Concord', 'Free Holds of {n}'],
  Horde: ['{n} Horde', 'Host of {n}', 'Clans of {n}', '{n} Warband'],
};

const FALLBACK_KINDS = ['valley', 'hold'], FALLBACK_TITLES = ['{n}'];

/** The place name a realm is built on, asked of the naming style of its people. */
export function realmNameRequest(seed: number, polity: number, form: string, race: string, culture: string): NameRequest {
  const kinds = NAME_KINDS[form] ?? FALLBACK_KINDS;
  const h = hash2(seed, 0x7e11 + polity);
  return { kind: kinds[h % kinds.length], seed: h & 0x7fffffff, race, culture, context: 0 };
}

/** The realm's full title around its base name. */
export function realmTitle(seed: number, polity: number, form: string, name: string): string {
  const titles = TITLES[form] ?? FALLBACK_TITLES;
  return titles[hash2(seed, 0x7171 + polity) % titles.length].replace('{n}', name);
}
