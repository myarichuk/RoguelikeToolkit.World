/**
 * What a history run left, kept with the document so the history panel works after Open (when the engine holds nothing):
 * the realms, the event log, and the territory as a base map plus the changes that replay over it.
 */
export interface PolityRecord {
  id: number;
  /** `PolityForm` name: Kingdom, MerchantRepublic, LooseAssociation, Horde. */
  form: string;
  /** The seat's tile. */
  capital: number;
  founded: number;
  /** Turn it ended, -1 while it stands. */
  ended: number;
  /** The polity it broke away from, -1 for an original. */
  parent: number;
  /** Why it ended (a HistoryStrings entry), empty while it stands. */
  endCause: string;
  folk: string;
  culture: string;
  /** The conqueror's culture it renders through, if any. */
  overlay: string;
  /** Display name, made from the seat's naming style. */
  name: string;
  /** Index into the document's styles that names its places, -1 none. */
  style: number;
}

export interface EventRecord {
  id: number;
  /** `HistoryEventKind` name. */
  kind: string;
  turn: number;
  polity: number;
  /** Where it happened, -1 nowhere in particular. */
  tile: number;
  /** The event id that caused it, -1 none. */
  cause: number;
  importance: number;
  /** Plugin-defined kind (`namespace/name`), empty for built-ins. */
  plugin: string;
  tags: string[];
  /**
   * Who took part, when anyone did (absent otherwise, as on every legacy event): an entity id and its role. The role's
   * prefix names the entity: `person:commander` is a person, `settlement:besieged` a settlement, a bare role a realm.
   */
  parts?: { entity: number; role: string }[];
}

/**
 * The history expansion's records (docs/HISTORY_EXPANSION_PLAN.md §7): persons, their bonds and offices, battles and route
 * states, as the engine exports them (strides in persons.ts, battles.ts, routes.ts). Present only when a run wrote any, so a
 * world generated with every expansion feature off keeps exactly the record (and `.wmap`) it always had.
 */
export interface ExpansionRecord {
  /** The catalog's interned strings: relation kinds, offices, death causes, siege ends. */
  strings: string[];
  /** `PersonKind`, `BattleKind` and `BattleOutcome` names by value. */
  personKinds: string[];
  battleKinds: string[];
  battleOutcomes: string[];
  /** Persons (`PERSON_STRIDE` ints each), their `folk\tculture\tepithets` text, and their names (made by the editor). */
  persons: Int32Array;
  personText: string[];
  personNames: string[];
  relations: Int32Array;
  tenures: Int32Array;
  battles: Int32Array;
  /** Per battle: `terrain\tattackerTactic\tattackerUnit\tdefenderTactic\tdefenderUnit\tbeats`. */
  battleText: string[];
  routeStates: Int32Array;
  /** Every route sample by turn, then route. */
  routeSeries: Int32Array;
  /** The engine's `HistoryRoads()` segments: where each route runs, for the route-safety overlay. */
  roads: Int32Array;
  /** Tile of each history settlement by index (sieges name the besieged place by index). */
  settlementTiles: Int32Array;
  /** Rendered prose per event, aligned with `events`, when the engine has a chronicle; absent otherwise. */
  chronicle?: string[];
}

/** The typed arrays of an expansion record, which travel as blobs in a `.wmap`; the string tables go in the header. */
export const EXPANSION_ARRAYS = ['persons', 'relations', 'tenures', 'battles', 'routeStates', 'routeSeries', 'roads', 'settlementTiles'] as const;
export type ExpansionArray = (typeof EXPANSION_ARRAYS)[number];
export type ExpansionRecordJson = Omit<ExpansionRecord, ExpansionArray>;

/** A run's output. The typed arrays travel as blobs in a `.wmap`; everything else is JSON. */
export interface HistoryRecord {
  /** Turns simulated, and years per turn. */
  turns: number;
  turnYears: number;
  polities: PolityRecord[];
  events: EventRecord[];
  /** Owner polity per tile at turn 0 (-1 unclaimed). */
  ownerBase: Int32Array;
  /** `(turn, tile, owner)` triples by turn: from that turn on the tile belongs to `owner`. */
  changes: Int32Array;
  /** Persons, battles and route states, when the run had any expansion feature on. */
  expansion?: ExpansionRecord;
}

/** The record without its typed arrays, as written into a `.wmap` header. */
export type HistoryRecordJson = Omit<HistoryRecord, 'ownerBase' | 'changes' | 'expansion'> & { expansion?: ExpansionRecordJson };

/** A realm's colour as 0..255 RGB: golden-angle hues keep neighbouring ids apart. The map, the list and the borders share it. */
export function realmRgb(id: number): [number, number, number] {
  const h = ((id * 137.508) % 360) / 60, s = 0.62, l = 0.52, c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs((h % 2) - 1)), m = l - c / 2;
  const [r, g, b] = h < 1 ? [c, x, 0] : h < 2 ? [x, c, 0] : h < 3 ? [0, c, x] : h < 4 ? [0, x, c] : h < 5 ? [x, 0, c] : [c, 0, x];
  return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
}

export const realmColor = (id: number, alpha = 1): string => `rgba(${realmRgb(id).join(', ')}, ${alpha})`;

/** Owner per tile at the end of `turn` (-1 = before any turn: the base map). */
export function ownersAt(h: HistoryRecord, turn: number, into?: Int32Array): Int32Array {
  const out = into && into.length === h.ownerBase.length ? into : new Int32Array(h.ownerBase.length);
  out.set(h.ownerBase);
  const c = h.changes;
  for (let i = 0; i < c.length && c[i] <= turn; i += 3) out[c[i + 1]] = c[i + 2];
  return out;
}

/** A realm's display name, with a stand-in for an id the record doesn't know. */
export const realmName = (h: HistoryRecord, id: number): string => h.polities.find((p) => p.id === id)?.name ?? `Realm ${id}`;

/** Whether a polity stands at the end of `turn`. */
export const standsAt = (p: PolityRecord, turn: number): boolean => p.founded <= turn && (p.ended < 0 || p.ended > turn);

const KIND_TEXT: Record<string, string> = {
  Founding: 'founded', Fork: 'timeline forked', SiteLost: 'lost a site', War: 'went to war', Conquest: 'conquered', Rebellion: 'rebelled',
  RoadBuilt: 'opened a trade route', RoadCut: 'lost a trade route', Exchange: 'trade diffused culture', Treaty: 'made a treaty', Grudge: 'took a grudge',
  HolyFound: 'raised a holy site', Desecration: 'desecrated a holy site', Villain: 'saw a tyrant rise', Ultimatum: 'issued an ultimatum', Tribute: 'paid tribute',
  Myth: 'told a myth', SettlementFounded: 'founded a settlement', SettlementEnded: 'lost a settlement', Shock: 'was struck by disaster',
  SettlementCaptured: 'took a settlement', SettlementTierChanged: 'saw a settlement change', PolityCollapsed: 'collapsed', PolityAbsorbed: 'was absorbed', Plugin: 'event',
  Siege: 'laid a siege', PersonRose: 'saw someone rise', PersonDied: 'mourned a death', Intrigue: 'plotted', CaravanRaided: 'lost a caravan to raiders',
  OutlawRose: 'saw an outlaw band rise', OutlawEnded: 'saw an outlaw band end', Smuggling: 'saw smugglers at work', Privateering: 'sent out privateers',
  Campaign: 'campaigned', Prophecy: 'heard a prophecy',
};

/** Events that change the political map; the default log shows only these. */
export const MAJOR_KINDS: ReadonlySet<string> = new Set(['Founding', 'War', 'Conquest', 'Rebellion', 'PolityCollapsed', 'PolityAbsorbed', 'HolyFound', 'Desecration', 'Shock', 'Villain', 'SettlementCaptured', 'Myth', 'Siege']);

export const kindText = (kind: string): string => KIND_TEXT[kind] ?? kind;

/** The value of a `key:value` tag, if the event has one. */
export const tagValue = (e: EventRecord, key: string): string | undefined => {
  const t = e.tags.find((x) => x.startsWith(`${key}:`));
  return t === undefined ? undefined : t.slice(key.length + 1);
};

const ROLE_TEXT: Record<string, string> = { Capital: 'a capital', Colony: 'a colony', Node: 'a trade node', Post: 'a garrison post', Shrine: 'a shrine town', Refuge: 'a refuge', Waystation: 'a waystation' };
/** A numeric `key:id` tag (a realm id), if the event has one. */
export const tagNumber = (e: EventRecord, key: string): number | undefined => {
  const n = Number(tagValue(e, key));
  return tagValue(e, key) !== undefined && Number.isFinite(n) ? n : undefined;
};

/** `elf_wood` as "Elf wood": a key as a label. */
export const humanize = (key: string): string => key.replace(/_/g, ' ').replace(/^./, (c) => c.toUpperCase());

const CAUSE_TEXT: Record<string, string> = {
  war: 'war', plague: 'plague', famine: 'famine', drought: 'drought', quake: 'an earthquake', flood: 'a flood', eruption: 'an eruption', abandoned: 'abandonment', razed: 'being razed',
};
/** What a risen villain wants (Core's VillainOperator agendas). */
const AGENDA_TEXT: Record<string, string> = { dominion: 'dominion', ruin: 'ruin', hunger: 'endless hunger', shadow: 'the shadow' };

/** An end cause as it reads after "by": `razed` becomes "being razed". */
export const causeText = (key: string): string => CAUSE_TEXT[key] ?? (key.replace(/_/g, ' ') || 'unknown causes');

/** `attacker_decisive` (an `outcome:` tag) in words. */
const OUTCOME_TEXT: Record<string, string> = {
  attacker_decisive: 'a rout of the defenders', attacker_victory: 'a victory for the attackers', attacker_pyrrhic: 'a costly victory for the attackers',
  stalemate: 'a stalemate', defender_victory: 'the defenders holding', defender_decisive: 'a rout of the attackers', averted: 'no battle',
};
const outcomeText = (e: EventRecord): string => {
  const o = tagValue(e, 'outcome');
  return o ? `, ending in ${OUTCOME_TEXT[o] ?? o.replace(/_/g, ' ')}` : '';
};

/**
 * A plain-words line for the log: the realm, what it did, and what else the tags say. `personOf` names a person id (the
 * expansion's `person:`, `band:`, `agent:` tags); without it a person reads as "someone".
 */
export function eventLine(e: EventRecord, nameOf: (polity: number) => string, personOf?: (person: number) => string): string {
  const who = e.polity >= 0 ? nameOf(e.polity) : 'The world';
  const other = (key: string) => {
    const id = tagNumber(e, key);
    return id === undefined ? undefined : nameOf(id);
  };
  const person = (key: string) => {
    const id = tagNumber(e, key);
    return id === undefined ? undefined : personOf ? personOf(id) : 'someone';
  };
  switch (e.kind) {
    case 'War': { const a = other('attacker'), d = other('defender'); return a && d ? `${a} attacked ${d}${outcomeText(e)}` : `${who} went to war`; }
    case 'Campaign': { const a = other('attacker'), d = other('defender'); return a && d ? `${a} marched on ${d} but never gave battle${e.tags.includes('declined') ? ' (declined the field)' : e.tags.includes('supply_failed') ? ' (supply failed)' : ''}` : `${who} campaigned`; }
    case 'Siege': {
      const a = other('attacker'), d = other('defender'), end = tagValue(e, 'end');
      return a && d ? `${a} besieged a settlement of ${d}${end ? `: ${end.replace(/_/g, ' ')}` : ''}` : `${who} laid a siege`;
    }
    case 'PersonRose': {
      const p = person('person') ?? 'someone', office = tagValue(e, 'office');
      if (e.tags.includes('merchant')) return `${p} rose as a merchant prince of ${who}`;
      return office ? `${p} became ${humanize(office).toLowerCase()} of ${who}` : `${p} rose in ${who}`;
    }
    case 'PersonDied': {
      const p = person('person') ?? 'someone', cause = e.tags.find((t) => !t.includes(':') && t !== 'deceased');
      return `${p} of ${who} died${cause ? ` (${cause.replace(/_/g, ' ')})` : ''}`;
    }
    case 'Intrigue': {
      const plot = tagValue(e, 'plot'), target = other('target'), actor = other('actor') ?? who;
      const exposed = e.tags.includes('exposed') || e.tags.includes('caught');
      return `${actor} ${exposed ? 'was caught plotting' : 'plotted'}${plot ? ` (${plot.replace(/_/g, ' ')})` : ''}${target ? ` against ${target}` : ''}`;
    }
    case 'CaravanRaided': {
      const band = person('band');
      return `${band ? `${band}'s band` : 'Brigands'} raided a caravan of ${other('victim') ?? who}`;
    }
    case 'OutlawRose': return `${person('person') ?? 'An outlaw'} raised a band on the road${tagValue(e, 'source') ? ` (${tagValue(e, 'source')!.replace(/_/g, ' ')})` : ''}`;
    case 'OutlawEnded': return `${person('person') ?? 'An outlaw'}'s band came to an end${tagValue(e, 'end') ? ` (${tagValue(e, 'end')!.replace(/_/g, ' ')})` : ''}`;
    case 'Privateering': {
      const p = person('privateer') ?? person('person') ?? 'a privateer', prey = other('victim');
      return e.tags.includes('commission') ? `${who} commissioned ${p}${prey ? ` against ${prey}` : ''}` : `${p} took a prize${prey ? ` from ${prey}` : ''} for ${who}`;
    }
    case 'Smuggling': return `smugglers worked a cut road of ${who}`;
    case 'Conquest': { const w = other('winner'), l = other('loser'); return w && l ? `${w} conquered ${l}` : `${who} conquered a rival`; }
    case 'Rebellion': { const p = other('parent'); return p ? `${who} broke away from ${p}` : `${who} rebelled`; }
    case 'PolityAbsorbed': { const into = other('into'); return into ? `${who} was absorbed by ${into}` : `${who} was absorbed`; }
    case 'SettlementCaptured': { const from = other('from'); return from ? `${who} took a settlement from ${from}` : `${who} took a settlement`; }
    case 'SettlementFounded': { const role = ROLE_TEXT[tagValue(e, 'role') ?? '']; return `${who} founded ${role ?? 'a settlement'}`; }
    case 'SettlementEnded': { const cause = e.tags.find((t) => !t.includes(':')); return `${who} lost a settlement${cause ? ` to ${causeText(cause)}` : ''}`; }
    case 'SettlementTierChanged': {
      const to = tagValue(e, 'to')?.toLowerCase();
      const grew = e.tags.includes('up');
      return `a settlement of ${who} ${grew ? 'grew into' : 'shrank to'} ${to ? `a ${to}` : 'another size'}`;
    }
    case 'Villain': {
      const agenda = tagValue(e, 'agenda');
      return `${who} fell under a tyrant${agenda ? ` bent on ${AGENDA_TEXT[agenda] ?? agenda}` : ''}`;
    }
    case 'Myth': {
      const faith = tagValue(e, 'faith'), powers = e.tags.filter((t) => t.startsWith('power:')).map((t) => t.slice(6).replace(/_/g, ' '));
      return `${who} told the myth of its ${faith ? humanize(faith).toLowerCase() : ''} faith${powers.length ? `, of ${powers.join(' and ')}` : ''}`.replace('its  faith', 'its faith');
    }
    case 'Plugin': return `${who}: ${e.plugin}`;
    default: return `${who} ${kindText(e.kind)}`;
  }
}
