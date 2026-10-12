import type { Editor } from '../engine/Editor';
import { CLIMATE_MODEL_DEFAULTS, CLIMATE_MODEL_LIMITS, DEFAULT_STYLES, EARTH, PLANET_LIMITS, styleKey } from '../engine/world/protocol';
import type { DocSettings } from '../engine/doc/types';
import type { ClimateModelSettings } from '../engine/world/protocol';
import { ViewModel } from './ViewModel';

export interface StyleOption { key: string; race: string; label: string; }

export interface GenerateState extends ClimateModelSettings {
  seed: number;
  size: number;
  plates: number;
  obliquity: number;
  flux: number;
  rotation: number;
  density: number;
  /** Selected `race|culture` keys; empty = the default mix. */
  styles: string[];
  available: StyleOption[];
  busy: boolean;
  /** True when a generator input differs from what the current document was generated or loaded with. */
  stale: boolean;
  warp: number;
  detail: number;
  ring: number;
}

/** World generation parameters (Core) and coastline stylization (document settings). */
export class GenerateViewModel extends ViewModel<GenerateState> {
  constructor(private readonly editor: Editor) {
    const s = editor.doc.settings;
    super({
      seed: 1337, size: 5, plates: 14, ...EARTH, ...CLIMATE_MODEL_DEFAULTS, density: 1, styles: [...DEFAULT_STYLES], available: [], busy: false, stale: false,
      warp: s.warp, detail: s.detail, ring: s.ring,
    });
    this.baseline = this.paramsKey();
    this.track(editor.events.on('content', () => this.syncStyles()));
    this.track(editor.events.on('busy', (b) => this.set({ busy: !!b })));
    this.track(editor.events.on('doc', (doc) => this.syncDoc(doc.settings)));
    this.syncStyles();
  }

  private baseline = '';

  private paramsKey(): string {
    const s = this.state;
    return JSON.stringify([s.seed, s.size, s.plates, s.obliquity, s.flux, s.rotation, s.climateRefresh, s.meridionalWind, s.windCellBlend, s.oceanHeat, s.oceanRain, s.aeolian, s.density, s.styles]);
  }

  /** Every setter funnels through here, so `stale` can never drift from the inputs. */
  protected override set(patch: Partial<GenerateState>): void {
    super.set(patch);
    super.set({ stale: this.paramsKey() !== this.baseline });
  }

  private syncDoc(s: DocSettings): void {
    this.set({ warp: s.warp, detail: s.detail, ring: s.ring, obliquity: s.obliquity, flux: s.flux, rotation: s.rotation, climateRefresh: s.climateRefresh, meridionalWind: s.meridionalWind, windCellBlend: s.windCellBlend, oceanHeat: s.oceanHeat, oceanRain: s.oceanRain, aeolian: s.aeolian });
    // A document was generated, opened or created: whatever the inputs say now is the new baseline.
    this.baseline = this.paramsKey();
    this.set({});
  }

  private syncStyles(): void {
    const available = this.editor.nameStyles.map((s) => ({ key: styleKey(s), race: s.race, label: s.label }));
    if (available.length !== this.state.available.length) this.set({ available });
  }

  setSeed = (seed: number): void => this.set({ seed: Math.max(0, Math.floor(seed) >>> 0) });
  randomSeed = (): void => this.set({ seed: (Math.random() * 2 ** 31) >>> 0 });
  setSize = (size: number): void => this.set({ size: Math.min(7, Math.max(3, Math.round(size))) });
  setPlates = (plates: number): void => this.set({ plates: Math.min(40, Math.max(2, Math.round(plates))) });
  private clampTo = (v: number, [lo, hi]: readonly [number, number]): number => Math.min(hi, Math.max(lo, Number.isFinite(v) ? v : lo));
  setObliquity = (v: number): void => this.set({ obliquity: this.clampTo(v, PLANET_LIMITS.obliquity) });
  setFlux = (v: number): void => this.set({ flux: this.clampTo(v, PLANET_LIMITS.flux) });
  setRotation = (v: number): void => this.set({ rotation: this.clampTo(v, PLANET_LIMITS.rotation) });
  setClimateRefresh = (v: boolean): void => this.set({ climateRefresh: v });
  setMeridionalWind = (v: number): void => this.set({ meridionalWind: this.clampTo(v, CLIMATE_MODEL_LIMITS.meridionalWind) });
  setWindCellBlend = (v: number): void => this.set({ windCellBlend: this.clampTo(v, CLIMATE_MODEL_LIMITS.windCellBlend) });
  setOceanHeat = (v: number): void => this.set({ oceanHeat: this.clampTo(v, CLIMATE_MODEL_LIMITS.oceanHeat) });
  setOceanRain = (v: number): void => this.set({ oceanRain: this.clampTo(v, CLIMATE_MODEL_LIMITS.oceanRain) });
  setAeolian = (v: number): void => this.set({ aeolian: Math.round(this.clampTo(v, CLIMATE_MODEL_LIMITS.aeolian)) });
  resetModel = (): void => this.set({ ...CLIMATE_MODEL_DEFAULTS });
  resetPlanet = (): void => this.set({ ...EARTH });
  setDensity = (density: number): void => this.set({ density });

  toggleStyle = (key: string): void => {
    const has = this.state.styles.includes(key);
    this.set({ styles: has ? this.state.styles.filter((k) => k !== key) : [...this.state.styles, key] });
  };

  /** Select or clear every culture of one race at once, keeping the rest of the selection. */
  setRace = (race: string, on: boolean): void => {
    const keys = this.state.available.filter((o) => o.race === race).map((o) => o.key);
    const rest = this.state.styles.filter((k) => !keys.includes(k));
    this.set({ styles: on ? [...rest, ...keys] : rest });
  };

  generate = async (): Promise<void> => {
    const s = this.state;
    await this.editor.generate({ seed: s.seed, size: s.size, plates: s.plates, planet: { obliquity: s.obliquity, flux: s.flux, rotation: s.rotation }, model: { climateRefresh: s.climateRefresh, meridionalWind: s.meridionalWind, windCellBlend: s.windCellBlend, oceanHeat: s.oceanHeat, oceanRain: s.oceanRain, aeolian: s.aeolian }, styles: s.styles, title: this.editor.doc.settings.title || undefined, density: s.density }).catch(() => {});
  };

  /** Coastline look: live, undoable (one step per slider scrub). */
  setCoast = (patch: Partial<Pick<GenerateState, 'warp' | 'detail' | 'ring'>>): void => {
    this.set(patch);
    this.editor.setSettings(patch, `coast:${Object.keys(patch).join(',')}`);
  };
}
