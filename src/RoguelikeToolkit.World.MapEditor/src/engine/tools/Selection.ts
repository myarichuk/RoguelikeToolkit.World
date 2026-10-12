import { Emitter } from '../core/Emitter';

export interface SelectionState {
  /** Inspected map feature (river, range, sea, settlement, deposit...). */
  feature: string | null;
  hoverFeature: string | null;
  stamps: ReadonlySet<string>;
  labels: ReadonlySet<string>;
}

/** Observable selection, shared by tools, overlay and inspector view model. */
export class Selection {
  private s: SelectionState = { feature: null, hoverFeature: null, stamps: new Set(), labels: new Set() };
  readonly events = new Emitter<{ change: SelectionState }>();

  get state(): SelectionState { return this.s; }

  set(patch: Partial<SelectionState>): void {
    const next = { ...this.s, ...patch };
    if (next.feature === this.s.feature && next.hoverFeature === this.s.hoverFeature && next.stamps === this.s.stamps && next.labels === this.s.labels) return;
    this.s = next;
    this.events.emit('change', next);
  }

  clear(): void { this.set({ feature: null, stamps: new Set(), labels: new Set() }); }
  get isEmpty(): boolean { return !this.s.feature && !this.s.stamps.size && !this.s.labels.size; }
}
