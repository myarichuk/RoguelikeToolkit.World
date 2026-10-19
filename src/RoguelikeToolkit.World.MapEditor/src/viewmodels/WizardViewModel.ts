import type { Editor } from '../engine/Editor';
import { DEFAULT_MS_PER_TILE, PLANET_PRESETS, TECTONIC_PRESETS, tilesOf } from '../engine/world/presets';
import type { GenerateViewModel } from './GenerateViewModel';
import { ViewModel } from './ViewModel';

export const WIZARD_STEPS = [
  { id: 'planet', label: 'Planet' },
  { id: 'land', label: 'Land' },
  { id: 'peoples', label: 'Peoples' },
  { id: 'realms', label: 'Realms' },
  { id: 'look', label: 'Look' },
  { id: 'review', label: 'Create' },
] as const;

export interface WizardState {
  open: boolean;
  step: number;
  /** This device's measured generation cost, ms per tile (a default until a world has been timed). */
  msPerTile: number;
  /** Whether the wizard opens when the editor starts. */
  showOnStart: boolean;
}

const get = (k: string): string | null => { try { return localStorage.getItem(`wme.${k}`); } catch { return null; } };
const set = (k: string, v: string): void => { try { localStorage.setItem(`wme.${k}`, v); } catch { /* storage unavailable */ } };

/** The "new world" wizard: a guided tour over the generator's choices. It edits the Generate view model and creates the world from it. */
export class WizardViewModel extends ViewModel<WizardState> {
  constructor(editor: Editor, private readonly generateVm: GenerateViewModel) {
    const speed = Number(get('msPerTile'));
    super({ open: false, step: 0, msPerTile: Number.isFinite(speed) && speed > 0 ? speed : DEFAULT_MS_PER_TILE, showOnStart: get('wizard') !== 'off' });
    // Time every generation, so the estimates shown for the big detail levels come from this device and not from a laptop.
    this.track(editor.events.on('timings', (t) => {
      const size = editor.doc.settings.size;
      if (!(t.totalMs > 0) || !(t.generateMs > 0)) return;
      const msPerTile = t.totalMs / tilesOf(size);
      set('msPerTile', String(msPerTile));
      this.set({ msPerTile });
    }));
  }

  /** Whether the editor should open the wizard instead of generating a world at once. */
  get opensOnStart(): boolean { return this.state.showOnStart; }

  show = (step = 0): void => this.set({ open: true, step });
  close = (): void => this.set({ open: false });
  goto = (step: number): void => this.set({ step: Math.min(WIZARD_STEPS.length - 1, Math.max(0, step)) });
  next = (): void => this.goto(this.state.step + 1);
  back = (): void => this.goto(this.state.step - 1);
  setShowOnStart = (on: boolean): void => { set('wizard', on ? 'on' : 'off'); this.set({ showOnStart: on }); };

  /** Rolls everything the wizard can: a planet, a land, a seed. Peoples and pins stay as the author left them. */
  randomize = (): void => {
    const pick = <T,>(list: readonly T[]): T => list[Math.floor(Math.random() * list.length)];
    this.generateVm.applyPlanetPreset(pick(PLANET_PRESETS));
    this.generateVm.applyTectonics(pick(TECTONIC_PRESETS));
    this.generateVm.randomSeed();
  };

  /** Creates the world from the choices and closes the wizard when it has worked. */
  create = async (): Promise<void> => {
    this.close();
    await this.generateVm.generate();
  };
}
