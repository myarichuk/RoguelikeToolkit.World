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
      seed: 1337, size: 5, plates: 14, ...EARTH, ...CLIMATE_MODEL_DEFAULTS, title: 'Untitled world', density: 1, styles: [...DEFAULT_STYLES], available: [], busy: false,
      warp: s.warp, detail: s.detail, ring: s.ring,
    });
    this.track(editor.events.on('content', () => this.syncStyles()));
    this.track(editor.events.on('busy', (b) => this.set({ busy: !!b })));
    this.track(editor.events.on('doc', (doc) => this.syncDoc(doc.settings)));
    this.syncStyles();
  }

  private syncDoc(s: DocSettings): void {
    this.set({ warp: s.warp, detail: s.detail, ring: s.ring, obliquity: s.obliquity, flux: s.flux, rotation: s.rotation, climateRefresh: s.climateRefresh, meridionalWind: s.meridionalWind, oceanHeat: s.oceanHeat, oceanRain: s.oceanRain, aeolian: s.aeolian });
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
  setOceanHeat = (v: number): void => this.set({ oceanHeat: this.clampTo(v, CLIMATE_MODEL_LIMITS.oceanHeat) });
  setOceanRain = (v: number): void => this.set({ oceanRain: this.clampTo(v, CLIMATE_MODEL_LIMITS.oceanRain) });
  setAeolian = (v: number): void => this.set({ aeolian: Math.round(this.clampTo(v, CLIMATE_MODEL_LIMITS.aeolian)) });
  resetModel = (): void => this.set({ ...CLIMATE_MODEL_DEFAULTS });
  resetPlanet = (): void => this.set({ ...EARTH });
  setTitle = (title: string): void => this.set({ title });
  setDensity = (density: number): void => this.set({ density });

  toggleStyle = (key: string): void => {
    const has = this.state.styles.includes(key);
    this.set({ styles: has ? this.state.styles.filter((k) => k !== key) : [...this.state.styles, key] });
  };

  generate = async (): Promise<void> => {
    const s = this.state;
    await this.editor.generate({ seed: s.seed, size: s.size, plates: s.plates, planet: { obliquity: s.obliquity, flux: s.flux, rotation: s.rotation }, model: { climateRefresh: s.climateRefresh, meridionalWind: s.meridionalWind, oceanHeat: s.oceanHeat, oceanRain: s.oceanRain, aeolian: s.aeolian }, styles: s.styles, title: s.title, density: s.density }).catch(() => {});
  };

  /** Coastline look: live, undoable (one step per slider scrub). */
  setCoast = (patch: Partial<Pick<GenerateState, 'warp' | 'detail' | 'ring'>>): void => {
    this.set(patch);
    this.editor.setSettings(patch, `coast:${Object.keys(patch).join(',')}`);
  };
}
