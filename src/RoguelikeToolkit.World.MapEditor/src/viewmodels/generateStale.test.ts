import { describe, expect, it } from 'vitest';
import type { Editor } from '../engine/Editor';
import { DEFAULT_SETTINGS } from '../engine/doc/types';
import { GenerateViewModel } from './GenerateViewModel';

/** Just enough Editor for the view model: an event bus, the settings, and the name styles. */
function fakeEditor() {
  const handlers = new Map<string, ((v?: unknown) => void)[]>();
  const editor = {
    doc: { settings: { ...DEFAULT_SETTINGS } },
    nameStyles: [],
    peoples: [],
    formNames: [],
    historyFeatures: [{ name: 'Persons', bit: 1 }, { name: 'Battles', bit: 2 }],
    events: { on: (n: string, f: (v?: unknown) => void) => { handlers.set(n, [...(handlers.get(n) ?? []), f]); return () => {}; } },
    setSettings: () => {},
  };
  return { editor: editor as unknown as Editor, emit: (n: string, v?: unknown) => (handlers.get(n) ?? []).forEach((f) => f(v)), settings: editor.doc.settings };
}

describe('GenerateViewModel.stale', () => {
  it('flags a changed generator input and clears when a document is attached', () => {
    const { editor, emit, settings } = fakeEditor();
    const vm = new GenerateViewModel(editor);
    expect(vm.state.stale).toBe(false);
    vm.setObliquity(40);
    expect(vm.state.stale).toBe(true);
    emit('doc', { settings });
    expect(vm.state.stale).toBe(false);
  });

  it('clears when the input is put back, and ignores cosmetic coastline edits', () => {
    const { editor } = fakeEditor();
    const vm = new GenerateViewModel(editor);
    vm.setSeed(7);
    expect(vm.state.stale).toBe(true);
    vm.setSeed(1337);
    expect(vm.state.stale).toBe(false);
    vm.setCoast({ warp: 9 });
    expect(vm.state.stale).toBe(false);
  });
});

describe('GenerateViewModel.setRace', () => {
  it('selects and clears one race without touching the others', () => {
    const { editor, emit } = fakeEditor();
    (editor as unknown as { nameStyles: unknown[] }).nameStyles = [
      { race: 'A', culture: '1', label: 'A · One' }, { race: 'A', culture: '2', label: 'A · Two' }, { race: 'B', culture: '1', label: 'B · One' },
    ];
    const vm = new GenerateViewModel(editor);
    emit('content');
    vm.toggleStyle('B|1');
    const before = [...vm.state.styles];
    vm.setRace('A', true);
    expect(vm.state.styles).toEqual(expect.arrayContaining(['A|1', 'A|2', ...before]));
    vm.setRace('A', false);
    expect(vm.state.styles.some((k) => k.startsWith('A|'))).toBe(false);
    expect(vm.state.styles).toEqual(before);
  });
});

describe('GenerateViewModel realms', () => {
  const make = () => new GenerateViewModel(fakeEditor().editor);

  it('marks the world stale when a founder is pinned, and clears when it is removed', () => {
    const vm = make();
    vm.addFounder();
    expect(vm.state.stale).toBe(true);
    expect(vm.state.history.founders).toHaveLength(1);
    vm.removeFounder(0);
    expect(vm.state.stale).toBe(false);
  });

  it('keeps founders within the realm count and drops the surplus when it shrinks', () => {
    const vm = make();
    vm.setMaxPolities(3);
    for (let i = 0; i < 5; i++) vm.addFounder();
    expect(vm.state.history.founders).toHaveLength(3);
    vm.setMaxPolities(2);
    expect(vm.state.history.founders).toHaveLength(2);
  });

  it('toggles a people in the roster and pins a founder to one', () => {
    const vm = make();
    vm.togglePeople('elf_wood|elven_wood');
    expect(vm.state.history.roster).toEqual([{ folk: 'elf_wood', culture: 'elven_wood' }]);
    vm.togglePeople('elf_wood|elven_wood');
    expect(vm.state.history.roster).toEqual([]);
    vm.addFounder();
    vm.setFounderPeople(0, 'orc|orcish_horde');
    vm.updateFounder(0, { form: 3, lat: 12, lon: -30 });
    expect(vm.state.history.founders[0]).toEqual({ folk: 'orc', culture: 'orcish_horde', form: 3, lat: 12, lon: -30 });
  });
});

describe('GenerateViewModel realms and name styles', () => {
  it('adds a name style of the folk\'s race when a people is chosen, so its places are named in its tongue', () => {
    const { editor } = fakeEditor();
    (editor as unknown as { nameStyles: unknown[] }).nameStyles = [{ race: 'orc', culture: 'fantasy_common', label: 'Orc' }, { race: 'elf_dark', culture: 'fantasy_common', label: 'Dark elf' }];
    const vm = new GenerateViewModel(editor);
    expect(vm.state.styles.some((k) => k.startsWith('elf_dark|'))).toBe(false);
    vm.togglePeople('elf_dark|elven_dark');
    expect(vm.state.styles).toContain('elf_dark|fantasy_common');
    vm.addFounder();
    vm.setFounderPeople(0, 'orc|orcish_horde');
    expect(vm.state.styles).toContain('orc|fantasy_common');
  });
});

describe('GenerateViewModel history features', () => {
  it('leaves the features to the engine until one is ticked, then starts from none', () => {
    const { editor } = fakeEditor();
    const vm = new GenerateViewModel(editor);
    expect(vm.state.historyFeatures.map((f) => f.name)).toEqual(['Persons', 'Battles']);
    expect(vm.state.history.features).toBeUndefined();
    vm.toggleFeature(2);
    expect(vm.state.history.features).toBe(2);
    expect(vm.state.stale).toBe(true);
    vm.toggleFeature(1);
    vm.toggleFeature(2);
    expect(vm.state.history.features).toBe(1);
    vm.allFeatures();
    expect(vm.state.history.features).toBe(3);
    vm.defaultFeatures();
    expect('features' in vm.state.history).toBe(false);
    expect(vm.state.stale).toBe(false);
  });
});
