import { describe, expect, it } from 'vitest';
import type { Editor } from '../engine/Editor';
import { DEFAULT_VIEW } from '../engine/render/ViewSettings';
import { history } from '../engine/world/history/expansionFixture';
import type { HistoryRecord } from '../engine/world/history/record';
import { PersonCardViewModel } from './PersonCardViewModel';

/** Just enough Editor for the card: a document with a history, the view, and an event bus. */
function fakeEditor(h: HistoryRecord | null) {
  const handlers = new Map<string, (() => void)[]>();
  let view = { ...DEFAULT_VIEW, historyTurn: 4 };
  const editor = {
    doc: { history: h, world: { tileXY: new Float32Array(200) } },
    view: () => view,
    setView: (v: typeof view) => { view = v; (handlers.get('view') ?? []).forEach((f) => f()); },
    focus: () => {},
    events: { on: (n: string, f: () => void) => { handlers.set(n, [...(handlers.get(n) ?? []), f]); return () => {}; } },
  };
  return editor as unknown as Editor;
}

const withRealms = (): HistoryRecord => ({
  ...history(),
  polities: [0, 1].map((id) => ({ id, form: 'Kingdom', capital: 0, founded: 0, ended: -1, parent: -1, endCause: '', folk: 'human', culture: 'human_common', overlay: '', name: `Realm ${id}`, style: 0 })),
});

describe('PersonCardViewModel', () => {
  it('opens a card with offices, bonds and the scrubbed turn', () => {
    const vm = new PersonCardViewModel(fakeEditor(withRealms()));
    vm.show(1);
    const s = vm.state;
    expect(s.open).toBe(true);
    expect(s.turn).toBe(4);
    expect(s.card).toMatchObject({ id: 1, name: 'Name 1', kind: 'General', realm: 'Realm 0', living: true, born: 2, died: 10 });
    expect(s.card!.offices.map((o) => `${o.office}@${o.realm}`)).toEqual(['Marshal@Realm 0', 'Ruler@Realm 0']);
    expect(s.card!.relations).toEqual([{ kind: 'Patron', other: { id: 0, name: 'Name 0' }, from: 2, to: -1, outgoing: false }]);
  });

  it('follows a bond to another card and walks back along the trail', () => {
    const vm = new PersonCardViewModel(fakeEditor(withRealms()));
    vm.show(1);
    vm.show(0);
    expect(vm.state.card!.id).toBe(0);
    expect(vm.state.trail).toEqual([{ id: 1, name: 'Name 1' }]);
    expect(vm.state.card!.died).toBe(6);
    expect(vm.state.card!.deathCause).toBe('Age');
    vm.back();
    expect(vm.state.card!.id).toBe(1);
    expect(vm.state.trail).toEqual([]);
    vm.close();
    expect(vm.state.open).toBe(false);
  });

  it('names who backs an agent', () => {
    const vm = new PersonCardViewModel(fakeEditor(withRealms()));
    vm.show(2);
    expect(vm.state.card!.sponsor).toEqual({ realm: 'Realm 1', person: { id: 3, name: 'Person 3' } });
  });

  it('has no card without the expansion, or for an unknown person', () => {
    const vm = new PersonCardViewModel(fakeEditor({ ...withRealms(), expansion: undefined }));
    vm.show(1);
    expect(vm.state.open).toBe(false);
    const vm2 = new PersonCardViewModel(fakeEditor(withRealms()));
    vm2.show(99);
    expect(vm2.state.open).toBe(false);
  });
});
