import { tagNumber, tagValue, type EventRecord, type ExpansionRecord } from './record';

/** Ints per route state in `HistoryRouteStates()` and per sample in `HistoryRouteSeries()`. */
export const ROUTE_STATE_STRIDE = 10;
export const ROUTE_SAMPLE_STRIDE = 5;

/** A trade route's state at the end of history: what guards it, who preys on it. Endpoints are its two capitals' tiles. */
export interface RouteState {
  a: number;
  b: number;
  /** 0..1. */
  safety: number;
  traffic: number;
  forts: number;
  waystations: number;
  /** Person leading the band that works it, -1 none. */
  outlawBand: number;
  /** Realm secretly paying that band, -1 none. */
  sponsor: number;
  sabotagedTurn: number;
  lastIncidentTurn: number;
}

/** How safe and busy a route was at one turn. */
export interface RouteSample { safety: number; traffic: number; }

/** A route's key, as the `route:{a}-{b}` event tag writes it (smaller tile first). */
export const routeKey = (a: number, b: number): string => `${Math.min(a, b)}-${Math.max(a, b)}`;

/** The route an event's `route:` tag names, as a key. */
export const routeOfEvent = (e: EventRecord): string | undefined => {
  const v = tagValue(e, 'route');
  if (v === undefined) return undefined;
  const [a, b] = v.split('-').map(Number);
  return Number.isFinite(a) && Number.isFinite(b) ? routeKey(a, b) : undefined;
};

export function decodeRouteStates(x: ExpansionRecord): RouteState[] {
  const r = x.routeStates, out: RouteState[] = [];
  for (let o = 0; o + ROUTE_STATE_STRIDE <= r.length; o += ROUTE_STATE_STRIDE) {
    out.push({
      a: r[o], b: r[o + 1], safety: r[o + 2] / 1000, traffic: r[o + 3] / 1000, forts: r[o + 4], waystations: r[o + 5],
      outlawBand: r[o + 6], sponsor: r[o + 7], sabotagedTurn: r[o + 8], lastIncidentTurn: r[o + 9],
    });
  }
  return out;
}

/**
 * Every route's safety and traffic as of `turn`: its latest sample at or before it (samples come by turn, then route).
 * A route not yet sampled by then is absent.
 */
export function routeSafetyAt(series: ArrayLike<number>, turn: number): Map<string, RouteSample> {
  const out = new Map<string, RouteSample>();
  for (let o = 0; o + ROUTE_SAMPLE_STRIDE <= series.length; o += ROUTE_SAMPLE_STRIDE) {
    if (series[o] > turn) break;
    out.set(routeKey(series[o + 1], series[o + 2]), { safety: series[o + 3] / 1000, traffic: series[o + 4] / 1000 });
  }
  return out;
}

/** An outlaw band's lair: where it hid, whose band it was, which road it worked. */
export interface Haven { tile: number; person: number; route: string | undefined; rose: number; /** Turn the band ended, -1 while it lasts. */ ended: number; }

/** Every band's haven (from `OutlawRose` events' `haven:` tags) and when the band ended (`OutlawEnded`). */
export function outlawHavens(events: readonly EventRecord[]): Haven[] {
  const out: Haven[] = [];
  const ended = new Map<number, number>();
  for (const e of events) {
    if (e.kind !== 'OutlawEnded') continue;
    const p = tagNumber(e, 'person');
    if (p !== undefined && !ended.has(p)) ended.set(p, e.turn);
  }
  for (const e of events) {
    if (e.kind !== 'OutlawRose') continue;
    const tile = tagNumber(e, 'haven'), person = tagNumber(e, 'person') ?? -1;
    if (tile === undefined || tile < 0) continue;
    out.push({ tile, person, route: routeOfEvent(e), rose: e.turn, ended: ended.get(person) ?? -1 });
  }
  return out;
}

/** The havens of bands working at the end of `turn`. */
export const havensAt = (havens: readonly Haven[], turn: number): Haven[] => havens.filter((h) => h.rose <= turn && (h.ended < 0 || h.ended > turn));

/** A raid on a caravan: where, on which road, by whose band (-1 nameless brigands), against which realm. */
export interface Raid { tile: number; route: string | undefined; band: number; victim: number; event: number; }

/** The caravan raids of one turn. */
export function raidsAt(events: readonly EventRecord[], turn: number): Raid[] {
  const out: Raid[] = [];
  for (const e of events) {
    if (e.kind !== 'CaravanRaided' || e.turn !== turn || e.tile < 0) continue;
    out.push({ tile: e.tile, route: routeOfEvent(e), band: tagNumber(e, 'band') ?? -1, victim: tagNumber(e, 'victim') ?? e.polity, event: e.id });
  }
  return out;
}

/** A safety heat colour: red where travel is deadly, through amber, to green where it is safe. */
export function safetyColor(safety: number, alpha = 1): string {
  const s = Math.min(1, Math.max(0, safety));
  const hue = Math.round(s * 120);
  return `hsla(${hue}, 75%, 45%, ${alpha})`;
}
