import type { Editor } from '../engine/Editor';
import { depositLabel } from '../engine/world/features';
import type { CapacityKind } from '../engine/world/physics/capacity';
import type { ClimateKind } from '../engine/world/physics/climate';
import { DEPOSIT_TYPES, FEATURE_KINDS, type DepositType, type FeatureKind } from '../engine/world/protocol';
import { DEPOSIT_MARKERS } from '../engine/render/Overlay';
import { VIEW_PRESETS, type BaseMode, type Layers, type ViewSettings } from '../engine/render/ViewSettings';
import { ViewModel } from './ViewModel';

export interface FilterRow<K extends string> { key: K; label: string; count: number; on: boolean; color?: string; symbol?: string; }

export interface ViewFiltersState {
  view: ViewSettings;
  presets: { id: string; label: string; active: boolean }[];
  kinds: FilterRow<FeatureKind>[];
  deposits: FilterRow<DepositType>[];
}

const KIND_LABEL: Record<FeatureKind, string> = {
  capital: 'Capitals', city: 'Cities', town: 'Towns', village: 'Villages', fort: 'Forts', ruin: 'Ruins', shrine: 'Holy sites', battlefield: 'Battlefields', river: 'Rivers', lake: 'Lakes', sea: 'Seas',
  ocean: 'Oceans', mountain: 'Mountain ranges', valley: 'Valleys & canyons', forest: 'Forests', deposit: 'Resources',
};

/**
 * View presets ("Plates", "Plates + topography", ...), layer toggles, and feature/resource
 * filters. Filters gate what is drawn *and* what can be picked by the inspector.
 */
export class ViewFiltersViewModel extends ViewModel<ViewFiltersState> {
  private docOff: (() => void) | null = null;

  constructor(private readonly editor: Editor) {
    super({ view: editor.view(), presets: [], kinds: [], deposits: [] });
    this.track(editor.events.on('view', () => this.sync()));
    const watch = () => { this.docOff?.(); this.docOff = editor.doc.features.events.on('change', () => this.sync()); this.sync(); };
    this.track(editor.events.on('doc', watch));
    this.track(() => this.docOff?.());
    watch();
  }

  private sync(): void {
    const v = this.editor.view();
    const kindCount = new Map<string, number>(), depCount = new Map<string, number>();
    for (const f of this.editor.doc.features.all()) {
      kindCount.set(f.kind, (kindCount.get(f.kind) ?? 0) + 1);
      if (f.deposit) depCount.set(f.deposit.type, (depCount.get(f.deposit.type) ?? 0) + 1);
    }
    this.set({
      view: v,
      presets: VIEW_PRESETS.map((p) => ({ id: p.id, label: p.label, active: sameLook(p.apply(v), v) })),
      kinds: FEATURE_KINDS.map((k) => ({ key: k, label: KIND_LABEL[k], count: kindCount.get(k) ?? 0, on: v.kinds[k] })),
      deposits: DEPOSIT_TYPES.map((d) => ({ key: d, label: depositLabel(d), count: depCount.get(d) ?? 0, on: v.deposits[d], color: DEPOSIT_MARKERS[d].color, symbol: DEPOSIT_MARKERS[d].symbol })),
    });
  }

  private update(fn: (v: ViewSettings) => ViewSettings): void { this.editor.setView(fn(this.editor.view())); }

  applyPreset = (id: string): void => this.editor.applyPreset(id);
  setBase = (base: BaseMode): void => this.update((v) => ({ ...v, base }));
  setCapacityKind = (capacityKind: CapacityKind): void => this.update((v) => ({ ...v, capacityKind }));
  setClimateKind = (climateKind: ClimateKind): void => this.update((v) => ({ ...v, climateKind }));
  setPlateTopo = (plateTopo: number): void => this.update((v) => ({ ...v, plateTopo }));
  toggleLayer = (key: keyof Layers): void => this.update((v) => ({ ...v, layers: { ...v.layers, [key]: !v.layers[key] } }));
  toggleKind = (k: FeatureKind): void => this.update((v) => ({
    ...v, kinds: { ...v.kinds, [k]: !v.kinds[k] },
    // Showing resources implies the resource layer.
    layers: k === 'deposit' && !v.kinds.deposit ? { ...v.layers, resources: true } : v.layers,
  }));
  toggleDeposit = (d: DepositType): void => this.update((v) => ({ ...v, deposits: { ...v.deposits, [d]: !v.deposits[d] } }));
  setAllDeposits = (on: boolean): void => this.update((v) => ({ ...v, deposits: Object.fromEntries(DEPOSIT_TYPES.map((d) => [d, on])) as Record<DepositType, boolean> }));
}

const sameLook = (a: ViewSettings, b: ViewSettings) =>
  a.base === b.base && a.plateTopo === b.plateTopo && a.historyTurn === b.historyTurn && a.capacityKind === b.capacityKind && a.climateKind === b.climateKind && JSON.stringify(a.layers) === JSON.stringify(b.layers);
