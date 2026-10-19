import type { Editor } from '../engine/Editor';
import type { LabelStyle, MapStyle, PaletteToken, Theme } from '../engine/content/types';
import { ViewModel } from './ViewModel';

export interface ThemeEditorState {
  theme: Theme;
  edited: boolean;
  biomeNames: Record<string, string>;
}

/** Map style fields the editor exposes, with UI ranges. Colors are detected by value. */
export const MAP_NUMBERS: { key: keyof MapStyle; label: string; min: number; max: number; step: number }[] = [
  { key: 'coastWidth', label: 'Coast width', min: 0, max: 2, step: 0.05 },
  { key: 'haloWidth', label: 'Shallows reach', min: 0, max: 12, step: 0.25 },
  { key: 'rippleCount', label: 'Ripple rings', min: 0, max: 8, step: 1 },
  { key: 'rippleOpacity', label: 'Ripple opacity', min: 0, max: 1, step: 0.05 },
  { key: 'hillshade', label: 'Hillshade', min: 0, max: 1, step: 0.02 },
  { key: 'grain', label: 'Paper grain', min: 0, max: 0.2, step: 0.005 },
  { key: 'stains', label: 'Stains', min: 0, max: 0.3, step: 0.01 },
  { key: 'innerCoast', label: 'Inner coast shade', min: 0, max: 1, step: 0.05 },
  { key: 'riverWidth', label: 'River width', min: 0.2, max: 3, step: 0.05 },
  { key: 'riverInkWidth', label: 'River bank ink', min: 0, max: 1, step: 0.02 },
  { key: 'roadWidth', label: 'Road width', min: 0.3, max: 3, step: 0.05 },
];

export const MAP_COLORS: { key: keyof MapStyle; label: string }[] = [
  { key: 'paper', label: 'Paper' }, { key: 'sea', label: 'Sea' }, { key: 'shallow', label: 'Shallows' }, { key: 'seaInk', label: 'Sea ink' },
  { key: 'landInk', label: 'Land ink' }, { key: 'river', label: 'Rivers' }, { key: 'riverInk', label: 'River ink' }, { key: 'road', label: 'Road ink' }, { key: 'frame', label: 'Frame' },
];

/**
 * Live theme editing. Every change writes a full theme into the user overlay pack (same id, so
 * it overrides the shipped theme), the registry notifies, the editor re-applies — colors and
 * line weights update on the next frame; stamp inks rebuild the atlas (debounced). "Reset"
 * drops the override; "Export" saves the overlay as a normal content pack.
 */
export class ThemeEditorViewModel extends ViewModel<ThemeEditorState> {
  constructor(private readonly editor: Editor) {
    super({ theme: editor.theme(), edited: editor.packs.isEdited(editor.themeId), biomeNames: {} });
    const sync = () => this.set({
      theme: editor.theme(), edited: editor.packs.isEdited(editor.themeId),
      biomeNames: Object.fromEntries(editor.packs.biomes().map((b) => [String(b.id), b.name])),
    });
    this.track(editor.events.on('theme', sync));
    this.track(editor.events.on('content', sync));
    sync();
  }

  private edit(fn: (t: Theme) => Theme): void {
    const next = fn(structuredClone(this.state.theme));
    this.set({ theme: next, edited: true });
    this.editor.editTheme(next);
  }

  setMap = <K extends keyof MapStyle>(key: K, value: MapStyle[K]): void => this.edit((t) => ({ ...t, map: { ...t.map, [key]: value } }));
  setPalette = (token: PaletteToken, color: string): void => this.edit((t) => ({ ...t, palette: { ...t.palette, [token]: color } }));
  setBiome = (id: string, color: string): void => this.edit((t) => ({ ...t, biomes: { ...t.biomes, [id]: color } }));
  setLabel = (kind: string, patch: Partial<LabelStyle>): void => this.edit((t) => ({ ...t, labels: { ...t.labels, [kind]: { ...t.labels[kind], ...patch } } }));
  setUi = (key: string, color: string): void => this.edit((t) => ({ ...t, ui: { ...t.ui, [key]: color } }));
  rename = (name: string): void => this.edit((t) => ({ ...t, name }));

  /** A new theme (own id) starting from the current one. */
  duplicate = (): void => {
    const base = this.state.theme;
    let n = 2;
    while (this.editor.packs.themes().some((t) => t.id === `${base.id}-${n}`)) n++;
    const copy: Theme = { ...structuredClone(base), id: `${base.id}-${n}`, name: `${base.name} ${n}` };
    this.editor.editTheme(copy);
    this.editor.setTheme(copy.id);
  };

  reset = (): void => this.editor.packs.resetTheme(this.state.theme.id);

  /** The user overlay pack as JSON (themes edited in this session and earlier imports). */
  exportPack = (): Blob | null => {
    const p = this.editor.packs.userPack();
    return p ? new Blob([JSON.stringify(p, null, 2)], { type: 'application/json' }) : null;
  };

  /** Re-place labels after typography changes (sizes affect collisions). */
  relabel = (): void => this.editor.relabel();
}
