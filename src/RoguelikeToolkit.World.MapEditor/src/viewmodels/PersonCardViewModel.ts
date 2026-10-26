import type { Editor } from '../engine/Editor';
import { battlesOfPerson, decodeBattles, OUTCOME_LABELS, outcomeColor, wonBy, type Battle } from '../engine/world/history/battles';
import { personIndex, type Person, type PersonIndex } from '../engine/world/history/persons';
import { eventLine, humanize, realmColor, realmName, type HistoryRecord } from '../engine/world/history/record';
import { ViewModel } from './ViewModel';

/** Someone the card links to. */
export interface PersonLink { id: number; name: string; }

export interface PersonCard {
  id: number;
  name: string;
  /** "Ruler", "Merchant prince". */
  kind: string;
  /** Allegiance, empty for a free outlaw. */
  realm: string;
  color: string;
  folk: string;
  culture: string;
  epithets: string[];
  /** Born and died (or the end of history while living), in turns, against `turns`. */
  born: number;
  died: number;
  living: boolean;
  deathCause: string;
  /** Age at death or at the end, in years. */
  age: number;
  figures: [string, string][];
  offices: { office: string; realm: string; from: number; to: number }[];
  relations: { kind: string; other: PersonLink; from: number; to: number; outgoing: boolean }[];
  battles: { id: number; turn: number; kind: string; outcome: string; color: string; side: string; enemy: string; won: boolean | null; tile: number }[];
  /** Who backs an agent, a privateer or a band. */
  sponsor: { realm: string; person: PersonLink | null } | null;
  parent: PersonLink | null;
  events: { id: number; turn: number; text: string; tile: number }[];
}

export interface PersonCardState {
  open: boolean;
  /** Turns of history and the scrubbed turn, for the lifespan bar. */
  turns: number;
  turnYears: number;
  turn: number;
  card: PersonCard | null;
  /** Cards visited before this one, newest last (Back walks them). */
  trail: PersonLink[];
}

const EMPTY: PersonCardState = { open: false, turns: 0, turnYears: 0, turn: 0, card: null, trail: [] };

/** Most event lines a card lists. */
export const CARD_EVENTS = 40;

/**
 * The person card: one person's offices, bonds, battles and story, with links to the persons it names. Reads the record the
 * document keeps, so it works on an opened file; a world without the history expansion has no cards.
 */
export class PersonCardViewModel extends ViewModel<PersonCardState> {
  private person: number | null = null;
  private battles: { history: HistoryRecord; list: Battle[] } | null = null;

  constructor(private readonly editor: Editor) {
    super(EMPTY);
    this.track(editor.events.on('doc', () => this.sync()));
    this.track(editor.events.on('view', () => { if (this.state.open) this.sync(); }));
  }

  private battlesOf(h: HistoryRecord): Battle[] {
    if (this.battles?.history !== h) this.battles = { history: h, list: h.expansion ? decodeBattles(h.expansion) : [] };
    return this.battles.list;
  }

  private sync(): void {
    const h = this.editor.doc.history, ix = personIndex(h), p = this.person === null ? undefined : ix?.get(this.person);
    if (!h || !ix || !p) { this.set({ ...EMPTY }); this.person = null; return; }
    const view = this.editor.view(), turn = view.historyTurn < 0 ? h.turns : Math.min(view.historyTurn, h.turns);
    this.set({ open: true, turns: h.turns, turnYears: h.turnYears, turn, card: this.cardOf(h, ix, p) });
  }

  private cardOf(h: HistoryRecord, ix: PersonIndex, p: Person): PersonCard {
    const realm = (id: number) => (id >= 0 ? realmName(h, id) : '');
    const link = (id: number): PersonLink => ({ id, name: ix.nameOf(id) });
    const died = p.diedTurn < 0 ? h.turns : p.diedTurn;
    const sponsor = ix.sponsorOf(p.id);
    return {
      id: p.id, name: p.name, kind: humanize(p.kind.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()), realm: realm(p.polity), color: p.polity >= 0 ? realmColor(p.polity) : 'transparent',
      folk: humanize(p.folk), culture: humanize(p.culture), epithets: p.epithets.map(humanize),
      born: p.bornTurn, died, living: p.diedTurn < 0, deathCause: humanize(p.deathCause), age: Math.max(0, (died - p.bornTurn) * h.turnYears - p.bornYear),
      figures: [
        ['Martial', String(p.martial)], ['Intrigue', String(p.intrigue)], ['Stewardship', String(p.stewardship)], ['Charisma', String(p.charisma)],
        ['Renown', String(p.renown)], ...(p.wealth ? [['Wealth', p.wealth.toFixed(0)] as [string, string]] : []), ...(p.loyalty ? [['Loyalty', String(p.loyalty)] as [string, string]] : []),
      ],
      offices: ix.tenuresOf(p.id).map((t) => ({ office: humanize(t.office), realm: realm(t.polity), from: t.from, to: t.to })),
      relations: ix.relationsOf(p.id).map((r) => ({ kind: humanize(r.kind), other: link(r.a === p.id ? r.b : r.a), from: r.from, to: r.to, outgoing: r.a === p.id })),
      battles: battlesOfPerson(this.battlesOf(h), p.id).map((b) => {
        const attacker = b.attacker.general === p.id, enemy = attacker ? b.defender.polity : b.attacker.polity;
        return { id: b.id, turn: b.turn, kind: humanize(b.kind.toLowerCase()), outcome: OUTCOME_LABELS[b.outcome] ?? b.outcome, color: outcomeColor(b.outcome), side: attacker ? 'attacking' : 'defending', enemy: realm(enemy), won: wonBy(b, p.id), tile: b.tile };
      }),
      sponsor: sponsor ? { realm: realm(sponsor.polity), person: sponsor.person >= 0 ? link(sponsor.person) : null } : null,
      parent: p.parent >= 0 && ix.get(p.parent) ? link(p.parent) : null,
      events: ix.eventsOf(p.id).slice(0, CARD_EVENTS).map((e) => ({ id: e.id, turn: e.turn, text: eventLine(e, (id) => realmName(h, id), ix.nameOf), tile: e.tile })),
    };
  }

  /** Opens the card of a person; from another card, that one goes on the trail. */
  show = (id: number): void => {
    const from = this.state.open && this.state.card && this.state.card.id !== id ? [...this.state.trail, { id: this.state.card.id, name: this.state.card.name }] : this.state.open ? this.state.trail : [];
    this.person = id;
    this.set({ trail: from });
    this.sync();
  };

  /** Back to the previous card on the trail. */
  back = (): void => {
    const trail = this.state.trail, prev = trail[trail.length - 1];
    if (!prev) return;
    this.person = prev.id;
    this.set({ trail: trail.slice(0, -1) });
    this.sync();
  };

  close = (): void => { this.person = null; this.set({ ...EMPTY }); };

  /** Centres the map on a tile (a battle, an event) and keeps the card open. */
  showOnMap = (tile: number): void => {
    const xy = this.editor.doc.world.tileXY;
    if (tile >= 0 && tile * 2 + 1 < xy.length) this.editor.focus(xy[tile * 2], xy[tile * 2 + 1]);
  };

  /** Moves the history scrubber to a turn (a battle's, an office's), so the map shows it. */
  showTurn = (turn: number): void => {
    const h = this.editor.doc.history;
    if (!h) return;
    this.editor.setView({ ...this.editor.view(), historyTurn: turn >= h.turns ? -1 : Math.max(0, turn) });
  };
}
