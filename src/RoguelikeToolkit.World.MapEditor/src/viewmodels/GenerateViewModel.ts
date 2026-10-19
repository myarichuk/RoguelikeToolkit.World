import type { Editor } from '../engine/Editor';
import { CLIMATE_MODEL_DEFAULTS, CLIMATE_MODEL_LIMITS, DEFAULT_HISTORY, DEFAULT_STYLES, EARTH, PLANET_LIMITS, styleKey, type FounderPin, type HistorySettings, type PeopleOption } from '../engine/world/protocol';
import type { DocSettings } from '../engine/doc/types';
import type { ClimateModelSettings } from '../engine/world/protocol';
import { modelFor, type PlanetPreset, type TectonicPreset } from '../engine/world/presets';
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
  /** How history is run and who founds it. */
  history: HistorySettings;
  /** The peoples a world can be founded by, and the polity form names. */
  peoples: PeopleOption[];
  forms: string[];
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
      seed: 1337, size: 5, plates: 14, ...EARTH, ...CLIMATE_MODEL_DEFAULTS, density: 1, styles: [...DEFAULT_STYLES], available: [], history: structuredClone(DEFAULT_HISTORY), peoples: [], forms: [], busy: false, stale: false,
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
    return JSON.stringify([s.seed, s.size, s.plates, s.obliquity, s.flux, s.rotation, s.climateRefresh, s.meridionalWind, s.windCellBlend, s.oceanHeat, s.oceanRain, s.aeolian, s.density, s.styles, s.history]);
  }

  /** Every setter funnels through here, so `stale` can never drift from the inputs. */
  protected override set(patch: Partial<GenerateState>): void {
    super.set(patch);
    super.set({ stale: this.paramsKey() !== this.baseline });
  }

  private syncDoc(s: DocSettings): void {
    this.set({ history: structuredClone(s.history ?? DEFAULT_HISTORY), warp: s.warp, detail: s.detail, ring: s.ring, obliquity: s.obliquity, flux: s.flux, rotation: s.rotation, climateRefresh: s.climateRefresh, meridionalWind: s.meridionalWind, windCellBlend: s.windCellBlend, oceanHeat: s.oceanHeat, oceanRain: s.oceanRain, aeolian: s.aeolian });
    // A document was generated, opened or created: whatever the inputs say now is the new baseline.
    this.baseline = this.paramsKey();
    this.set({});
  }

  private syncPeoples(): void {
    const { peoples, formNames } = this.editor;
    if (peoples.length !== this.state.peoples.length) this.set({ peoples: [...peoples], forms: [...formNames] });
  }

  private syncStyles(): void {
    this.syncPeoples();
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
  /** A planet preset: its astronomy and the climate model it lists (the rest back to calibrated). */
  applyPlanetPreset = (p: PlanetPreset): void => this.set({ ...p.planet, ...modelFor(p) });
  applyTectonics = (t: TectonicPreset): void => this.set({ plates: t.plates, aeolian: t.aeolian });
  resetModel = (): void => this.set({ ...CLIMATE_MODEL_DEFAULTS });
  resetPlanet = (): void => this.set({ ...EARTH });
  setDensity = (density: number): void => this.set({ density });

  // --- Realms: how history runs and who founds it -------------------------------------------------
  private setHistory = (patch: Partial<HistorySettings>): void => this.set({ history: { ...this.state.history, ...patch } });
  setHistoryEnabled = (enabled: boolean): void => this.setHistory({ enabled });
  setTurns = (turns: number): void => this.setHistory({ turns: Math.min(200, Math.max(2, Math.round(turns))) });
  setYears = (years: number): void => this.setHistory({ years: Math.min(100, Math.max(1, Math.round(years))) });
  setMaxPolities = (n: number): void => {
    const maxPolities = Math.min(24, Math.max(1, Math.round(n)));
    this.setHistory({ maxPolities, founders: this.state.history.founders.slice(0, maxPolities) });
  };
  /** The people a pin or the roster names: `folk|culture`. */
  static peopleKey = (p: { folk: string; culture: string }): string => `${p.folk}|${p.culture}`;
  togglePeople = (key: string): void => {
    const roster = this.state.history.roster;
    const [folk, culture] = key.split('|');
    const has = roster.some((r) => r.folk === folk && r.culture === culture);
    this.set({ history: { ...this.state.history, roster: has ? roster.filter((r) => !(r.folk === folk && r.culture === culture)) : [...roster, { folk, culture }] }, ...(has ? {} : this.withNameStyle(folk)) });
  };
  /** A realm names its places in its folk's tongue, which needs a name style of that race among the chosen ones: add the first if none is. */
  private withNameStyle(folk: string): Partial<GenerateState> {
    if (!folk || this.state.styles.some((k) => k.startsWith(`${folk}|`))) return {};
    const first = this.state.available.find((o) => o.race === folk);
    return first ? { styles: [...this.state.styles, first.key] } : {};
  }
  clearRoster = (): void => this.setHistory({ roster: [] });
  addFounder = (): void => {
    const h = this.state.history;
    if (h.founders.length >= h.maxPolities) return;
    this.setHistory({ founders: [...h.founders, { folk: '', culture: '', form: -1, lat: null, lon: null }] });
  };
  updateFounder = (i: number, patch: Partial<FounderPin>): void =>
    this.setHistory({ founders: this.state.history.founders.map((f, k) => (k === i ? { ...f, ...patch } : f)) });
  /** Pins a founder to a people (`folk|culture`) or back to random (empty). */
  setFounderPeople = (i: number, key: string): void => {
    const [folk = '', culture = ''] = key.split('|');
    this.set({ history: { ...this.state.history, founders: this.state.history.founders.map((f, k) => (k === i ? { ...f, folk, culture } : f)) }, ...this.withNameStyle(folk) });
  };
  removeFounder = (i: number): void => this.setHistory({ founders: this.state.history.founders.filter((_, k) => k !== i) });
  resetRealms = (): void => this.set({ history: structuredClone(DEFAULT_HISTORY) });

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
    await this.editor.generate({ seed: s.seed, size: s.size, plates: s.plates, planet: { obliquity: s.obliquity, flux: s.flux, rotation: s.rotation }, model: { climateRefresh: s.climateRefresh, meridionalWind: s.meridionalWind, windCellBlend: s.windCellBlend, oceanHeat: s.oceanHeat, oceanRain: s.oceanRain, aeolian: s.aeolian }, styles: s.styles, history: s.history, title: this.editor.doc.settings.title || undefined, density: s.density }).catch(() => {});
  };

  /** Coastline look: live, undoable (one step per slider scrub). */
  setCoast = (patch: Partial<Pick<GenerateState, 'warp' | 'detail' | 'ring'>>): void => {
    this.set(patch);
    this.editor.setSettings(patch, `coast:${Object.keys(patch).join(',')}`);
  };
}
