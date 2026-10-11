import type { Editor } from '../engine/Editor';
import { DEFAULT_STYLES, styleKey } from '../engine/world/protocol';
import { ViewModel } from './ViewModel';

export interface StyleOption { key: string; race: string; label: string; }

export interface GenerateState {
  seed: number;
  size: number;
  plates: number;
  title: string;
  density: number;
  /** Selected `race|culture` keys; empty = the default mix. */
  styles: string[];
  available: StyleOption[];
  busy: boolean;
  warp: number;
  detail: number;
  ring: number;
}

/** World generation parameters (Core) and coastline stylization (document settings). */
export class GenerateViewModel extends ViewModel<GenerateState> {
  constructor(private readonly editor: Editor) {
    const s = editor.doc.settings;
    super({
      seed: 1337, size: 5, plates: 14, title: 'Untitled world', density: 1, styles: [...DEFAULT_STYLES], available: [], busy: false,
      warp: s.warp, detail: s.detail, ring: s.ring,
    });
    this.track(editor.events.on('content', () => this.syncStyles()));
    this.track(editor.events.on('busy', (b) => this.set({ busy: !!b })));
    this.track(editor.events.on('doc', (doc) => this.syncDoc(doc.settings)));
    this.syncStyles();
  }

  private syncDoc(s: { warp: number; detail: number; ring: number }): void {
    this.set({ warp: s.warp, detail: s.detail, ring: s.ring });
  }

  private syncStyles(): void {
    const available = this.editor.nameStyles.map((s) => ({ key: styleKey(s), race: s.race, label: s.label }));
    if (available.length !== this.state.available.length) this.set({ available });
  }

  setSeed = (seed: number): void => this.set({ seed: Math.max(0, Math.floor(seed) >>> 0) });
  randomSeed = (): void => this.set({ seed: (Math.random() * 2 ** 31) >>> 0 });
  setSize = (size: number): void => this.set({ size: Math.min(7, Math.max(3, Math.round(size))) });
  setPlates = (plates: number): void => this.set({ plates: Math.min(40, Math.max(2, Math.round(plates))) });
  setTitle = (title: string): void => this.set({ title });
  setDensity = (density: number): void => this.set({ density });

  toggleStyle = (key: string): void => {
    const has = this.state.styles.includes(key);
    this.set({ styles: has ? this.state.styles.filter((k) => k !== key) : [...this.state.styles, key] });
  };

  generate = async (): Promise<void> => {
    const s = this.state;
    await this.editor.generate({ seed: s.seed, size: s.size, plates: s.plates, styles: s.styles, title: s.title, density: s.density }).catch(() => {});
  };

  /** Coastline look: live, undoable (one step per slider scrub). */
  setCoast = (patch: Partial<Pick<GenerateState, 'warp' | 'detail' | 'ring'>>): void => {
    this.set(patch);
    this.editor.setSettings(patch, `coast:${Object.keys(patch).join(',')}`);
  };
}
