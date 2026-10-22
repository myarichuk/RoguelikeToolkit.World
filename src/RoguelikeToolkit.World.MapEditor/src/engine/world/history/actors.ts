import { humanize, tagNumber, type EventRecord, type HistoryRecord, type PolityRecord } from './record';

/** Whose history is being read: a single realm, or every realm of one people (folk and culture) together. */
export type Scope = 'realm' | 'culture';

/** A realm, or a culture's realms taken together. */
export interface Actor {
  id: string;
  name: string;
  /** The realms it stands for. */
  polities: number[];
  /** The realm whose colour stands for it on the map. */
  colorOf: number;
}

/** What an actor did over the run: the figures achievements are awarded on. */
export interface ActorStats {
  /** The most tiles it held at once, and the turn it first held that many. */
  peakTiles: number;
  peakTurn: number;
  /** Turns on which at least one of its realms stood. */
  turnsStanding: number;
  conquests: number;
  /** Wars it started. */
  wars: number;
  settlementsFounded: number;
  settlementsTaken: number;
  holySites: number;
  /** Realms that broke away from it. */
  breakaways: number;
  /** The turn its last realm fell, null while any still stands. */
  fell: number | null;
}

/** The turns a realm stood, with `until` as the turn it fell or the end of history. */
export const standingSpan = (p: PolityRecord, turns: number): { from: number; until: number } => ({ from: p.founded, until: p.ended < 0 ? turns : p.ended });

/** The actors of a scope, in the order realms were founded. */
export function actorsOf(h: HistoryRecord, scope: Scope): Actor[] {
  if (scope === 'realm') return h.polities.map((p) => ({ id: String(p.id), name: p.name, polities: [p.id], colorOf: p.id }));
  const groups = new Map<string, Actor>();
  for (const p of h.polities) {
    const key = `${p.folk}|${p.culture}`;
    const group = groups.get(key);
    if (group) group.polities.push(p.id);
    else groups.set(key, { id: key, name: `${humanize(p.folk)} · ${humanize(p.culture)}`, polities: [p.id], colorOf: p.id });
  }
  return [...groups.values()];
}

/** The realms an event is about, besides the one that logged it. Tag names differ by kind: `from` is a realm for a captured town, a settlement for a founded one. */
function involved(e: EventRecord): number[] {
  const ids = e.polity >= 0 ? [e.polity] : [];
  const tag = (key: string) => {
    const id = tagNumber(e, key);
    if (id !== undefined) ids.push(id);
  };
  switch (e.kind) {
    case 'War': tag('attacker'); tag('defender'); break;
    case 'Conquest': tag('winner'); tag('loser'); break;
    case 'Rebellion': tag('parent'); break;
    case 'PolityAbsorbed': tag('into'); break;
    case 'SettlementCaptured': tag('from'); break;
  }
  return ids;
}

/** The events that concern an actor's realms, oldest first. */
export function eventsOf(h: HistoryRecord, actor: Actor, keep: (e: EventRecord) => boolean = () => true): EventRecord[] {
  const mine = new Set(actor.polities);
  return h.events.filter((e) => keep(e) && involved(e).some((id) => mine.has(id)));
}

/** The figures for every actor, from the event log and the territory record. */
export function statsOf(h: HistoryRecord, actors: readonly Actor[]): Map<string, ActorStats> {
  const actorOfPolity = new Map<number, Actor>();
  for (const a of actors) for (const id of a.polities) actorOfPolity.set(id, a);
  const stats = new Map<string, ActorStats>(actors.map((a) => [a.id, {
    peakTiles: 0, peakTurn: 0, turnsStanding: 0, conquests: 0, wars: 0, settlementsFounded: 0, settlementsTaken: 0, holySites: 0, breakaways: 0, fell: null,
  }]));
  const of = (polity: number | undefined) => (polity === undefined ? undefined : stats.get(actorOfPolity.get(polity)?.id ?? ''));

  for (const e of h.events) {
    switch (e.kind) {
      case 'Conquest': { const s = of(tagNumber(e, 'winner')); if (s) s.conquests++; break; }
      case 'War': { const s = of(tagNumber(e, 'attacker')); if (s) s.wars++; break; }
      case 'SettlementFounded': { const s = of(e.polity); if (s) s.settlementsFounded++; break; }
      case 'SettlementCaptured': { const s = of(e.polity); if (s) s.settlementsTaken++; break; }
      case 'HolyFound': { const s = of(e.polity); if (s) s.holySites++; break; }
    }
  }
  for (const p of h.polities) { const s = of(p.parent >= 0 ? p.parent : undefined); if (s) s.breakaways++; }

  for (const a of actors) {
    const s = stats.get(a.id)!, mine = h.polities.filter((p) => a.polities.includes(p.id));
    for (let turn = 0; turn <= h.turns; turn++) if (mine.some((p) => p.founded <= turn && (p.ended < 0 || p.ended > turn))) s.turnsStanding++;
    s.fell = mine.every((p) => p.ended >= 0) ? Math.max(...mine.map((p) => p.ended)) : null;
  }

  // Territory: replay the changes turn by turn, keeping a running tile count per actor.
  const counts = new Map<string, number>(actors.map((a) => [a.id, 0]));
  const bump = (polity: number, by: number) => {
    const a = actorOfPolity.get(polity);
    if (a) counts.set(a.id, counts.get(a.id)! + by);
  };
  const peaks = (turn: number) => {
    for (const [id, n] of counts) { const s = stats.get(id)!; if (n > s.peakTiles) { s.peakTiles = n; s.peakTurn = turn; } }
  };
  const owner = h.ownerBase.slice();
  for (const o of owner) if (o >= 0) bump(o, 1);
  peaks(0);
  const c = h.changes;
  for (let i = 0; i < c.length; ) {
    const turn = c[i];
    for (; i < c.length && c[i] === turn; i += 3) {
      const tile = c[i + 1], next = c[i + 2];
      if (owner[tile] >= 0) bump(owner[tile], -1);
      if (next >= 0) bump(next, 1);
      owner[tile] = next;
    }
    peaks(turn);
  }
  return stats;
}
