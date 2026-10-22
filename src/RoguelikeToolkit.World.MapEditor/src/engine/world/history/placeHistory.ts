import { eventLine, realmName, type EventRecord, type HistoryRecord } from './record';

/** One line of a place's story: what happened there, and when. */
export interface PlaceEntry {
  turn: number;
  text: string;
}

/** A realm's hold on a tile, from `from` up to (not including) `until`; -1 means the end of history. */
export interface Rule {
  polity: number;
  from: number;
  until: number;
}

const byTileCache = new WeakMap<HistoryRecord, Map<number, EventRecord[]>>();

/** Every logged event by the tile it happened on, oldest first. Built once per record. */
export function eventsByTile(h: HistoryRecord): ReadonlyMap<number, EventRecord[]> {
  let index = byTileCache.get(h);
  if (!index) {
    index = new Map();
    for (const e of h.events) {
      if (e.tile < 0) continue;
      const list = index.get(e.tile);
      if (list) list.push(e); else index.set(e.tile, [e]);
    }
    byTileCache.set(h, index);
  }
  return index;
}

/** Who held a tile over the whole run, in order. Unclaimed stretches are left out. */
export function rulersOf(h: HistoryRecord, tile: number): Rule[] {
  const stints: Rule[] = [];
  let holder = h.ownerBase[tile] ?? -1, since = 0;
  const close = (turn: number) => { if (holder >= 0) stints.push({ polity: holder, from: since, until: turn }); };
  for (let i = 0; i < h.changes.length; i += 3) {
    if (h.changes[i + 1] !== tile || h.changes[i + 2] === holder) continue;
    close(h.changes[i]);
    holder = h.changes[i + 2];
    since = h.changes[i];
  }
  close(-1);
  return stints;
}

/**
 * The events worth telling about a place. A capital's tile logs every grudge and treaty of its realm; those belong to the
 * realm's own story, not the town's.
 */
const PLACE_EVENT_KINDS: ReadonlySet<string> = new Set([
  'Founding', 'SettlementFounded', 'SettlementEnded', 'SettlementCaptured', 'SettlementTierChanged', 'War', 'Conquest', 'Rebellion',
  'PolityCollapsed', 'PolityAbsorbed', 'HolyFound', 'Desecration', 'Shock', 'Villain', 'RoadBuilt', 'RoadCut',
]);

/** What happened on a tile, oldest first. Territory changes are told by `rulersOf`. */
export function placeStory(h: HistoryRecord, tile: number): PlaceEntry[] {
  return (eventsByTile(h).get(tile) ?? []).filter((e) => PLACE_EVENT_KINDS.has(e.kind)).map((e) => ({ turn: e.turn, text: eventLine(e, (id) => realmName(h, id)) }));
}

/** "Kingdom of X (turn 0 to 12), then Y League (from turn 12)": the rulers of a tile in words. */
export function rulersText(rules: readonly Rule[], nameOf: (polity: number) => string): string {
  return rules.map((r) => `${nameOf(r.polity)} (${r.until < 0 ? `from turn ${r.from}` : `turn ${r.from} to ${r.until}`})`).join(', then ');
}
