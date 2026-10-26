import type { Editor } from '../engine/Editor';
import type { Layers } from '../engine/render/ViewSettings';
import { battleOfEvent, battlesAt, decodeBattles, OUTCOME_LABELS, outcomeColor, type Battle } from '../engine/world/history/battles';
import { personIndex, personsOfEvent, type PersonIndex } from '../engine/world/history/persons';
import { eventLine, humanize, MAJOR_KINDS, ownersAt, realmColor, realmName, standsAt, type HistoryRecord } from '../engine/world/history/record';
import { havensAt, outlawHavens, raidsAt, type Haven } from '../engine/world/history/routes';
import { ViewModel } from './ViewModel';

export interface RealmRow {
  id: number; name: string; form: string; folk: string; culture: string; color: string;
  founded: number; ended: number; endCause: string; alive: boolean; parent: string; tiles: number;
}

export interface EventRow {
  id: number; turn: number; text: string; kind: string; polity: number; tile: number; major: boolean;
  /** The text is the chronicle's prose, not the editor's summary. */
  prose: boolean;
  /** The persons it names, for links to their cards. */
  persons: { id: number; name: string }[];
  /** The battle record it narrates: the outcome as a colour and words. */
  battle: { color: string; label: string } | null;
}

/** A person living at the scrubbed turn. */
export interface PersonRow { id: number; name: string; kind: string; office: string; realm: string; color: string; }

/** What the expansion shows on the map at the scrubbed turn. */
export interface TurnMarks { battles: number; sieges: number; raids: number; havens: number; }

/** Most rows the log draws; the count above it says how many matched. */
export const EVENT_ROWS = 200;
/** Most persons the list draws. */
export const PERSON_ROWS = 60;

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
  /** The run wrote expansion records (persons, battles, routes): the log is a chronicle and the map has more layers. */
  expansion: boolean;
  /** The log shows only the scrubbed turn. */
  turnOnly: boolean;
  persons: PersonRow[];
  personCount: number;
  marks: TurnMarks;
  battlesLayer: boolean;
  outlawsLayer: boolean;
  routeSafetyLayer: boolean;
}

const NO_MARKS: TurnMarks = { battles: 0, sieges: 0, raids: 0, havens: 0 };

const EMPTY: HistoryState = {
  has: false, turns: 0, turnYears: 0, turn: 0, realmsLayer: false, bordersLayer: false, realms: [], polity: null, majorOnly: true, events: [], eventCount: 0,
  expansion: false, turnOnly: false, persons: [], personCount: 0, marks: NO_MARKS, battlesLayer: false, outlawsLayer: false, routeSafetyLayer: false,
};

/** The expansion's records of one history, decoded once. */
interface Expansion { history: HistoryRecord; persons: PersonIndex; battles: Battle[]; havens: Haven[]; position: Map<number, number>; }

/**
 * The history panel: the realms that stood at the scrubbed turn, the event log up to it, and the controls that
 * move the territory tint. Everything comes from the record the document keeps, so it works after Open. A world with the
 * history expansion also lists its living persons, words its log through the chronicle and names who took part.
 */
export class HistoryViewModel extends ViewModel<HistoryState> {
  private history: HistoryRecord | null = null;
  private expansion: Expansion | null = null;

  constructor(private readonly editor: Editor) {
    super(EMPTY);
    this.track(editor.events.on('view', () => this.sync()));
    this.track(editor.events.on('doc', () => this.sync()));
    this.sync();
  }

  private expansionOf(h: HistoryRecord): Expansion | null {
    if (!h.expansion) return null;
    if (this.expansion?.history === h) return this.expansion;
    return (this.expansion = {
      history: h, persons: personIndex(h)!, battles: decodeBattles(h.expansion), havens: outlawHavens(h.events),
      position: new Map(h.events.map((e, i) => [e.id, i])),
    });
  }

  private sync(): void {
    const doc = this.editor.doc, h = doc.history, view = this.editor.view();
    if (h !== this.history) this.set({ polity: null });
    this.history = h;
    if (!h) { this.expansion = null; this.set({ ...EMPTY, majorOnly: this.state.majorOnly, turnOnly: this.state.turnOnly }); return; }
    const turn = view.historyTurn < 0 ? h.turns : Math.min(view.historyTurn, h.turns);
    const name = (id: number) => realmName(h, id);
    const owners = ownersAt(h, turn);
    const tiles = new Map<number, number>();
    for (let t = 0; t < owners.length; t++) if (owners[t] >= 0) tiles.set(owners[t], (tiles.get(owners[t]) ?? 0) + 1);
    const realms = h.polities.filter((p) => p.founded <= turn).map((p): RealmRow => ({
      id: p.id, name: p.name, form: p.form, folk: p.folk, culture: p.overlay || p.culture, color: realmColor(p.id), founded: p.founded,
      ended: p.ended, endCause: p.endCause, alive: standsAt(p, turn), parent: p.parent >= 0 ? name(p.parent) : '', tiles: tiles.get(p.id) ?? 0,
    })).sort((a, b) => Number(b.alive) - Number(a.alive) || b.tiles - a.tiles || a.founded - b.founded);
    const { polity, majorOnly, turnOnly } = this.state;
    const x = this.expansionOf(h), people = x?.persons;
    const concerns = (e: HistoryRecord['events'][number]) => polity === null || e.polity === polity || (!!x && e.tags.some((t) => /^(attacker|defender|target|actor|victim):/.test(t) && Number(t.slice(t.indexOf(':') + 1)) === polity));
    const matched = h.events.filter((e) => (turnOnly ? e.turn === turn : e.turn <= turn) && (!majorOnly || MAJOR_KINDS.has(e.kind)) && concerns(e));
    const events = matched.slice(-EVENT_ROWS).reverse().map((e): EventRow => {
      const prose = x ? h.expansion!.chronicle?.[x.position.get(e.id) ?? -1] ?? '' : '';
      const b = x ? battleOfEvent(x.battles, e) : undefined;
      return {
        id: e.id, turn: e.turn, text: prose || eventLine(e, name, people?.nameOf), kind: e.kind, polity: e.polity, tile: e.tile, major: MAJOR_KINDS.has(e.kind), prose: !!prose,
        persons: people ? personsOfEvent(e).filter((id) => people.get(id)).map((id) => ({ id, name: people.nameOf(id) })) : [],
        battle: b ? { color: outcomeColor(b.outcome), label: OUTCOME_LABELS[b.outcome] ?? b.outcome } : null,
      };
    });
    let persons: PersonRow[] = [], personCount = 0, marks = NO_MARKS;
    if (x) {
      const living = (polity === null ? people!.living(turn) : people!.personsOf(polity, turn)).sort((a, b) => b.renown - a.renown || a.id - b.id);
      personCount = living.length;
      persons = living.slice(0, PERSON_ROWS).map((p): PersonRow => {
        const office = people!.tenuresOf(p.id).filter((t) => t.from <= turn && (t.to < 0 || t.to > turn)).map((t) => humanize(t.office)).join(', ');
        return { id: p.id, name: p.name, kind: humanize(p.kind.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()), office, realm: p.polity >= 0 ? name(p.polity) : 'outlaw', color: p.polity >= 0 ? realmColor(p.polity) : 'transparent' };
      });
      const now = battlesAt(x.battles, turn);
      marks = { battles: now.filter((b) => b.kind !== 'Siege').length, sieges: now.filter((b) => b.kind === 'Siege').length, raids: raidsAt(h.events, turn).length, havens: havensAt(x.havens, turn).length };
    }
    this.set({
      has: true, turns: h.turns, turnYears: h.turnYears, turn, realmsLayer: view.layers.realms, bordersLayer: view.layers.borders, realms, events, eventCount: matched.length,
      expansion: !!x, persons, personCount, marks, battlesLayer: view.layers.battles, outlawsLayer: view.layers.outlaws, routeSafetyLayer: view.layers.routeSafety,
    });
  }

  setTurn = (turn: number): void => {
    const h = this.history;
    if (!h) return;
    // The end of history stays "latest", so a regenerated world with more turns keeps following it.
    this.editor.setView({ ...this.editor.view(), historyTurn: turn >= h.turns ? -1 : Math.max(0, Math.round(turn)) });
  };

  /** Turns a history layer on or off: the territory tint, the border outlines, or one of the expansion's marks. */
  setLayer = (key: keyof Pick<Layers, 'realms' | 'borders' | 'battles' | 'outlaws' | 'routeSafety'>, on: boolean): void => {
    const v = this.editor.view();
    this.editor.setView({ ...v, layers: { ...v.layers, [key]: on } });
  };

  setMajorOnly = (majorOnly: boolean): void => { this.set({ majorOnly }); this.sync(); };
  setTurnOnly = (turnOnly: boolean): void => { this.set({ turnOnly }); this.sync(); };
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
