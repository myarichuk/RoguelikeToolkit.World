import type { Editor } from '../engine/Editor';
import { awardAchievements, type Achievement } from '../engine/world/history/achievements';
import { actorsOf, eventsOf, standingSpan, statsOf, type Actor, type ActorStats, type Scope } from '../engine/world/history/actors';
import { personIndex } from '../engine/world/history/persons';
import { eventLine, MAJOR_KINDS, realmColor, realmName, type HistoryRecord } from '../engine/world/history/record';
import { ViewModel } from './ViewModel';

/** One row of the timeline chart and the list beside it. */
export interface ActorRow {
  id: string;
  name: string;
  color: string;
  /** The turns it stood, as bars on the chart. */
  spans: { from: number; until: number }[];
  /** Titles it holds. */
  titles: number;
  standing: boolean;
}

export interface LogLine { id: number; turn: number; text: string; tile: number; major: boolean; }

export interface ActorDetail {
  id: string;
  name: string;
  color: string;
  figures: [string, string][];
  achievements: Achievement[];
  events: LogLine[];
  eventCount: number;
}

export interface HistoryWindowState {
  open: boolean;
  has: boolean;
  scope: Scope;
  turns: number;
  actors: ActorRow[];
  selected: string | null;
  majorOnly: boolean;
  detail: ActorDetail | null;
}

/** Most event lines the window draws for one actor. */
export const LOG_ROWS = 300;

const EMPTY: HistoryWindowState = { open: false, has: false, scope: 'realm', turns: 0, actors: [], selected: null, majorOnly: true, detail: null };

/** What a scope makes of a history: its actors, their figures and the titles they won. Built once per (record, scope). */
interface Analysis {
  history: HistoryRecord;
  scope: Scope;
  actors: Actor[];
  stats: Map<string, ActorStats>;
  achievements: Achievement[];
}

const figuresOf = (s: ActorStats): [string, string][] => [
  ['Greatest extent', s.peakTiles ? `${s.peakTiles} tiles (turn ${s.peakTurn})` : 'none'],
  ['Turns standing', String(s.turnsStanding)],
  ['Conquests', String(s.conquests)],
  ['Wars started', String(s.wars)],
  ['Settlements founded', String(s.settlementsFounded)],
  ['Settlements taken', String(s.settlementsTaken)],
  ['Holy sites', String(s.holySites)],
  ['Realms broken away', String(s.breakaways)],
  ['Fate', s.fell === null ? 'still standing' : `gone by turn ${s.fell}`],
];

/**
 * The history window: a timeline of every realm (or every people), the titles they won, and the story of the one picked.
 * Reads only the record the document keeps, so it works on an opened file.
 */
export class HistoryWindowViewModel extends ViewModel<HistoryWindowState> {
  private analysis: Analysis | null = null;

  constructor(private readonly editor: Editor) {
    super(EMPTY);
    this.track(editor.events.on('doc', () => this.sync()));
  }

  private analyse(h: HistoryRecord, scope: Scope): Analysis {
    const a = this.analysis;
    if (a && a.history === h && a.scope === scope) return a;
    const actors = actorsOf(h, scope), stats = statsOf(h, actors);
    return (this.analysis = { history: h, scope, actors, stats, achievements: awardAchievements(actors, stats) });
  }

  private sync(): void {
    const h = this.editor.doc.history;
    if (!h) { this.analysis = null; this.set({ ...EMPTY, open: this.state.open, majorOnly: this.state.majorOnly, scope: this.state.scope }); return; }
    const { scope, majorOnly } = this.state, an = this.analyse(h, scope);
    const actor = an.actors.find((a) => a.id === this.state.selected) ?? an.actors[0], selected = actor?.id ?? null;
    const rows = an.actors.map((a): ActorRow => {
      const mine = h.polities.filter((p) => a.polities.includes(p.id));
      return {
        id: a.id, name: a.name, color: realmColor(a.colorOf), spans: mine.map((p) => standingSpan(p, h.turns)),
        titles: an.achievements.filter((x) => x.actor === a.id).length, standing: mine.some((p) => p.ended < 0),
      };
    });
    this.set({
      has: true, turns: h.turns, actors: rows, selected,
      detail: actor ? this.detailOf(an, actor, majorOnly) : null,
    });
  }

  private detailOf(an: Analysis, actor: Actor, majorOnly: boolean): ActorDetail {
    const h = an.history, nameOf = (id: number) => realmName(h, id), personOf = personIndex(h)?.nameOf;
    const matched = eventsOf(h, actor, (e) => !majorOnly || MAJOR_KINDS.has(e.kind));
    return {
      id: actor.id, name: actor.name, color: realmColor(actor.colorOf), figures: figuresOf(an.stats.get(actor.id)!),
      achievements: an.achievements.filter((x) => x.actor === actor.id),
      events: matched.slice(0, LOG_ROWS).map((e) => ({ id: e.id, turn: e.turn, text: eventLine(e, nameOf, personOf), tile: e.tile, major: MAJOR_KINDS.has(e.kind) })),
      eventCount: matched.length,
    };
  }

  show = (): void => { this.set({ open: true }); this.sync(); };
  close = (): void => this.set({ open: false });
  setScope = (scope: Scope): void => { this.set({ scope, selected: null }); this.sync(); };
  select = (id: string): void => { this.set({ selected: id }); this.sync(); };
  setMajorOnly = (majorOnly: boolean): void => { this.set({ majorOnly }); this.sync(); };

  /** Closes the window and centres the map on a tile of the world. */
  showOnMap = (tile: number): void => {
    const xy = this.editor.doc.world.tileXY;
    if (tile < 0 || tile * 2 + 1 >= xy.length) return;
    this.close();
    this.editor.focus(xy[tile * 2], xy[tile * 2 + 1]);
  };
}
