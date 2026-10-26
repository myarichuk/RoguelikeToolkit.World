import { hash2 } from '../../core/rng';
import { tagNumber, tagValue, type EventRecord, type ExpansionRecord, type HistoryRecord } from './record';

/** Ints per person in the engine's `HistoryPersons()` (WorldExports.PersonStride). */
export const PERSON_STRIDE = 25;
/** Ints per bond in `HistoryRelations()` and per tenure in `HistoryTenures()`. */
export const RELATION_STRIDE = 6;
export const TENURE_STRIDE = 5;

/** Prefix of an event participant role played by a person (Core's `ParticipantRoles.PersonPrefix`). */
export const PERSON_ROLE = 'person:';

/** A named person of history: a ruler, an heir, an office holder, a merchant prince, an outlaw, an agent. */
export interface Person {
  id: number;
  /** `PersonKind` name: Ruler, Heir, Advisor, General, Spymaster, MerchantPrince, RebelLeader, Outlaw, Privateer, Prophet, Agent. */
  kind: string;
  /** Allegiance, -1 for a free outlaw. */
  polity: number;
  /** Seat settlement index, -1 none. */
  seat: number;
  feminine: boolean;
  house: number;
  parent: number;
  bornTurn: number;
  /** Year inside the born turn. */
  bornYear: number;
  /** -1 while living. */
  diedTurn: number;
  /** Why they died (`age`, `battle`, `executed`...), empty while living. */
  deathCause: string;
  renown: number;
  wealth: number;
  loyalty: number;
  pledged: number;
  martial: number;
  intrigue: number;
  stewardship: number;
  charisma: number;
  /** OCEAN traits, 0–255. */
  traits: { openness: number; conscientiousness: number; extraversion: number; agreeableness: number; neuroticism: number };
  stableKey: number;
  folk: string;
  /** The Peoples culture key (`human_common`), as the name generator's culture rule reads it. */
  culture: string;
  /** Trait-table epithet ids (`cruel`, `beloved`). */
  epithets: string[];
  name: string;
}

/** A bond from `a` to `b` (`patron`, `rival`, `nemesis`, `agent_of`, `sponsor`, `pardoned`...). */
export interface Relation { a: number; b: number; kind: string; from: number; /** -1 while open. */ to: number; weight: number; }

/** An office held: `ruler`, `marshal`, `spymaster`... */
export interface Tenure { person: number; polity: number; office: string; from: number; /** -1 while held. */ to: number; }

const str = (x: ExpansionRecord, id: number): string => (id >= 0 ? x.strings[id] ?? '' : '');

/** The engine's person rows as records; `personText` and `personNames` are aligned with them. */
export function decodePersons(x: ExpansionRecord): Person[] {
  const p = x.persons, n = Math.floor(p.length / PERSON_STRIDE), out: Person[] = [];
  for (let i = 0; i < n; i++) {
    const o = i * PERSON_STRIDE;
    const [folk = '', culture = '', epithets = ''] = (x.personText[i] ?? '').split('\t');
    out.push({
      id: p[o], kind: x.personKinds[p[o + 1]] ?? '', polity: p[o + 2], seat: p[o + 3], feminine: p[o + 4] === 1, house: p[o + 5], parent: p[o + 6],
      bornTurn: p[o + 7], bornYear: p[o + 8], diedTurn: p[o + 9], deathCause: str(x, p[o + 10]), renown: p[o + 11], wealth: p[o + 12] / 100,
      loyalty: p[o + 13], pledged: p[o + 14] / 100, martial: p[o + 15], intrigue: p[o + 16], stewardship: p[o + 17], charisma: p[o + 18],
      traits: { openness: p[o + 19], conscientiousness: p[o + 20], extraversion: p[o + 21], agreeableness: p[o + 22], neuroticism: p[o + 23] },
      stableKey: p[o + 24], folk, culture, epithets: epithets.split(',').filter(Boolean), name: x.personNames[i] || `Person ${p[o]}`,
    });
  }
  return out;
}

export function decodeRelations(x: ExpansionRecord): Relation[] {
  const r = x.relations, out: Relation[] = [];
  for (let o = 0; o + RELATION_STRIDE <= r.length; o += RELATION_STRIDE) out.push({ a: r[o], b: r[o + 1], kind: str(x, r[o + 2]), from: r[o + 3], to: r[o + 4], weight: r[o + 5] });
  return out;
}

export function decodeTenures(x: ExpansionRecord): Tenure[] {
  const t = x.tenures, out: Tenure[] = [];
  for (let o = 0; o + TENURE_STRIDE <= t.length; o += TENURE_STRIDE) out.push({ person: t[o], polity: t[o + 1], office: str(x, t[o + 2]), from: t[o + 3], to: t[o + 4] });
  return out;
}

/** Whether a person lives at the end of `turn`: born by then, and not dead in it or before (a realm's `standsAt` rule). */
export const livingAt = (p: Person, turn: number): boolean => p.bornTurn <= turn && (p.diedTurn < 0 || p.diedTurn > turn);

/** Whether a bond or an office is open at the end of `turn`. */
export const openAt = (r: { from: number; to: number }, turn: number): boolean => r.from <= turn && (r.to < 0 || r.to > turn);

/**
 * The request that names a person: the same world seed, person, people and sex always give the same name, so a card
 * reads the same after Open. `key` is the person's stable key (Core's `HistoryPerson.StableKey`), not its id: ids shift
 * when a plugin adds persons, stable keys never do. Persons are named by folk under their culture's rule (the engine's
 * `NamePersons`).
 */
export interface PersonNameRequest { seed: number; folk: string; culture: string; feminine: boolean; }

export const personNameRequest = (seed: number, key: number, folk: string, culture: string, feminine: boolean): PersonNameRequest =>
  ({ seed: hash2(seed, 0x9e75 ^ key) & 0x7fffffff, folk, culture, feminine });

/** Person ids an event names through its participants whose role starts with `person:` (optionally one role, `person:outlaw`). */
export function involvedPersons(e: EventRecord, role?: string): number[] {
  const out: number[] = [];
  for (const p of e.parts ?? []) {
    if (!p.role.startsWith(PERSON_ROLE)) continue;
    if (role !== undefined && p.role !== PERSON_ROLE + role) continue;
    if (!out.includes(p.entity)) out.push(p.entity);
  }
  return out;
}

/** Event tags whose value is a person id. */
const PERSON_TAGS = ['person', 'band', 'agent', 'privateer', 'merchant'] as const;

/** Every person an event names: its `person:` participants first, then person-valued tags (`person:`, `band:`, `agent:`...). */
export function personsOfEvent(e: EventRecord): number[] {
  const out = involvedPersons(e);
  for (const key of PERSON_TAGS) {
    for (const t of e.tags) {
      if (!t.startsWith(`${key}:`)) continue;
      const id = Number(t.slice(key.length + 1));
      if (Number.isInteger(id) && id >= 0 && !out.includes(id)) out.push(id);
    }
  }
  return out;
}

/** Who backs an agent, a privateer or an outlaw band: a realm, and the person who runs them when there is one. */
export interface Sponsor { polity: number; person: number; }

/**
 * Everything about the persons of one record, decoded once and indexed: by id, by realm, bonds and offices per person.
 * Built through `personIndex`, which caches it per expansion record.
 */
export class PersonIndex {
  readonly persons: Person[];
  readonly relations: Relation[];
  readonly tenures: Tenure[];
  private readonly byId = new Map<number, Person>();
  private readonly byPolity = new Map<number, Person[]>();
  private readonly bonds = new Map<number, Relation[]>();
  private readonly offices = new Map<number, Tenure[]>();

  constructor(readonly history: HistoryRecord, x: ExpansionRecord) {
    this.persons = decodePersons(x);
    this.relations = decodeRelations(x);
    this.tenures = decodeTenures(x);
    const push = <K, V>(m: Map<K, V[]>, k: K, v: V) => { const l = m.get(k); if (l) l.push(v); else m.set(k, [v]); };
    for (const p of this.persons) { this.byId.set(p.id, p); if (p.polity >= 0) push(this.byPolity, p.polity, p); }
    for (const r of this.relations) { push(this.bonds, r.a, r); if (r.b !== r.a) push(this.bonds, r.b, r); }
    for (const t of this.tenures) push(this.offices, t.person, t);
  }

  get(id: number): Person | undefined { return this.byId.get(id); }
  nameOf = (id: number): string => this.byId.get(id)?.name ?? `Person ${id}`;

  /** A realm's persons, all of them or only those living at `turn`. */
  personsOf(polity: number, turn?: number): Person[] {
    const all = this.byPolity.get(polity) ?? [];
    return turn === undefined ? all : all.filter((p) => livingAt(p, turn));
  }

  living(turn: number): Person[] { return this.persons.filter((p) => livingAt(p, turn)); }
  relationsOf(id: number): Relation[] { return this.bonds.get(id) ?? []; }
  tenuresOf(id: number): Tenure[] { return this.offices.get(id) ?? []; }

  /** Who holds an office of a realm at `turn`. */
  holder(polity: number, office: string, turn: number): Person | undefined {
    const t = this.tenures.find((x) => x.polity === polity && x.office === office && openAt(x, turn));
    return t ? this.byId.get(t.person) : undefined;
  }

  /** The events a person takes part in, by participant or by a person-valued tag. */
  eventsOf(id: number): EventRecord[] {
    return this.history.events.filter((e) => personsOfEvent(e).includes(id));
  }

  /**
   * Who backs a person. An agent serves the realm it is a person of, run by the spymaster it is `agent_of`; a privateer holds
   * a `sponsor` bond from the ruler who commissioned it; an outlaw band's sponsor is the realm a sponsorship plot names (or a
   * route it works records). Null for anyone else, or a band nobody pays.
   */
  sponsorOf(id: number): Sponsor | null {
    const p = this.byId.get(id);
    if (!p) return null;
    if (p.kind === 'Agent') {
      const runner = this.relationsOf(id).find((r) => r.a === id && r.kind === 'agent_of');
      return { polity: p.polity, person: runner ? runner.b : -1 };
    }
    const bond = this.relationsOf(id).find((r) => r.b === id && r.kind === 'sponsor');
    if (bond) return { polity: this.byId.get(bond.a)?.polity ?? p.polity, person: bond.a };
    if (p.kind === 'Outlaw') {
      // A sponsorship names the band as `person:`, a sabotage it runs as `band:`; other plots name their victims as `person:`.
      const plot = this.history.events.find((e) => e.kind === 'Intrigue' && tagNumber(e, 'actor') !== undefined
        && (tagNumber(e, 'band') === id || ((tagValue(e, 'plot') ?? '').startsWith('sponsorship') && tagNumber(e, 'person') === id)));
      if (plot) return { polity: tagNumber(plot, 'actor')!, person: -1 };
    }
    return null;
  }
}

const indexes = new WeakMap<ExpansionRecord, PersonIndex>();

/** The person index of a record (built once per expansion record), or null when the run had no persons. */
export function personIndex(h: HistoryRecord | null | undefined): PersonIndex | null {
  const x = h?.expansion;
  if (!x || !h) return null;
  let ix = indexes.get(x);
  if (!ix || ix.history !== h) indexes.set(x, ix = new PersonIndex(h, x));
  return ix;
}
