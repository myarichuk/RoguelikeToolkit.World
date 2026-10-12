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
