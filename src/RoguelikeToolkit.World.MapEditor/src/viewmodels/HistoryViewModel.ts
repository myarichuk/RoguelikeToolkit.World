import type { Editor } from '../engine/Editor';
import { eventLine, MAJOR_KINDS, ownersAt, realmColor, realmName, standsAt, type HistoryRecord } from '../engine/world/history/record';
import { ViewModel } from './ViewModel';

export interface RealmRow {
  id: number; name: string; form: string; folk: string; culture: string; color: string;
  founded: number; ended: number; endCause: string; alive: boolean; parent: string; tiles: number;
}

export interface EventRow { id: number; turn: number; text: string; kind: string; polity: number; tile: number; major: boolean; }

/** Most rows the log draws; the count above it says how many matched. */
export const EVENT_ROWS = 200;

export interface HistoryState {
  /** The document has a history run to show. */
  has: boolean;
  turns: number;
  turnYears: number;
  /** The turn the scrubber shows (the end when the view says -1). */
  turn: number;
  realmsLayer: boolean;
  bordersLayer: boolean;
  realms: RealmRow[];
  /** Realm the log is narrowed to, null for all. */
  polity: number | null;
  majorOnly: boolean;
  events: EventRow[];
  eventCount: number;
}

const EMPTY: HistoryState = { has: false, turns: 0, turnYears: 0, turn: 0, realmsLayer: false, bordersLayer: false, realms: [], polity: null, majorOnly: true, events: [], eventCount: 0 };

/**
 * The history panel: the realms that stood at the scrubbed turn, the event log up to it, and the controls that
 * move the territory tint. Everything comes from the record the document keeps, so it works after Open.
 */
export class HistoryViewModel extends ViewModel<HistoryState> {
  private history: HistoryRecord | null = null;

  constructor(private readonly editor: Editor) {
    super(EMPTY);
    this.track(editor.events.on('view', () => this.sync()));
    this.track(editor.events.on('doc', () => this.sync()));
    this.sync();
  }

  private sync(): void {
    const doc = this.editor.doc, h = doc.history, view = this.editor.view();
    if (h !== this.history) this.set({ polity: null });
    this.history = h;
    if (!h) { this.set({ ...EMPTY, majorOnly: this.state.majorOnly }); return; }
    const turn = view.historyTurn < 0 ? h.turns : Math.min(view.historyTurn, h.turns);
    const name = (id: number) => realmName(h, id);
    const owners = ownersAt(h, turn);
    const tiles = new Map<number, number>();
    for (let t = 0; t < owners.length; t++) if (owners[t] >= 0) tiles.set(owners[t], (tiles.get(owners[t]) ?? 0) + 1);
    const realms = h.polities.filter((p) => p.founded <= turn).map((p): RealmRow => ({
      id: p.id, name: p.name, form: p.form, folk: p.folk, culture: p.overlay || p.culture, color: realmColor(p.id), founded: p.founded,
      ended: p.ended, endCause: p.endCause, alive: standsAt(p, turn), parent: p.parent >= 0 ? name(p.parent) : '', tiles: tiles.get(p.id) ?? 0,
    })).sort((a, b) => Number(b.alive) - Number(a.alive) || b.tiles - a.tiles || a.founded - b.founded);
    const { polity, majorOnly } = this.state;
    const matched = h.events.filter((e) => e.turn <= turn && (!majorOnly || MAJOR_KINDS.has(e.kind)) && (polity === null || e.polity === polity));
    const events = matched.slice(-EVENT_ROWS).reverse().map((e): EventRow => ({ id: e.id, turn: e.turn, text: eventLine(e, name), kind: e.kind, polity: e.polity, tile: e.tile, major: MAJOR_KINDS.has(e.kind) }));
    this.set({ has: true, turns: h.turns, turnYears: h.turnYears, turn, realmsLayer: view.layers.realms, bordersLayer: view.layers.borders, realms, events, eventCount: matched.length });
  }

  setTurn = (turn: number): void => {
    const h = this.history;
    if (!h) return;
    // The end of history stays "latest", so a regenerated world with more turns keeps following it.
    this.editor.setView({ ...this.editor.view(), historyTurn: turn >= h.turns ? -1 : Math.max(0, Math.round(turn)) });
  };

  /** Turns the territory tint or the border outlines on or off. */
  setLayer = (key: 'realms' | 'borders', on: boolean): void => {
    const v = this.editor.view();
    this.editor.setView({ ...v, layers: { ...v.layers, [key]: on } });
  };

  setMajorOnly = (majorOnly: boolean): void => { this.set({ majorOnly }); this.sync(); };
  setPolity = (polity: number | null): void => { this.set({ polity }); this.sync(); };

  /** Centers the map on a tile of the world. */
  focusTile = (tile: number): void => {
    const xy = this.editor.doc.world.tileXY;
    if (tile >= 0 && tile * 2 + 1 < xy.length) this.editor.focus(xy[tile * 2], xy[tile * 2 + 1]);
  };

  /** Centers the map on a realm's seat and filters the log to it. */
  selectRealm = (id: number): void => {
    const p = this.history?.polities.find((x) => x.id === id);
    if (!p) return;
    this.setPolity(this.state.polity === id ? null : id);
    this.focusTile(p.capital);
  };
}
