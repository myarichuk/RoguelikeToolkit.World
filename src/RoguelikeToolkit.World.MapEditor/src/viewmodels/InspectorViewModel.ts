import type { Editor } from '../engine/Editor';
import type { MapDocument } from '../engine/doc/MapDocument';
import { labelIdOf, type FeatureEntity, type LabelEntity, type StampEntity } from '../engine/doc/types';
import { placeStory, rulersOf, rulersText, type PlaceEntry } from '../engine/world/history/placeHistory';
import { realmName } from '../engine/world/history/record';
import { PLACE_KINDS } from '../engine/world/protocol';
import { ViewModel } from './ViewModel';

/** What history knows about a place: who ruled its tile, and what happened there. */
export interface PlaceHistory {
  rulers: string;
  story: PlaceEntry[];
}

export interface FeatureView {
  id: string;
  kind: string;
  name: string;
  culture: string;
  details: [string, string][];
  canReroll: boolean;
  hasLabel: boolean;
  /** Null for features history has nothing to say about (rivers, mountains, a world without history). */
  history: PlaceHistory | null;
}

export interface InspectorState {
  mode: 'none' | 'feature' | 'labels' | 'stamps';
  feature: FeatureView | null;
  /** First selected label (edits apply to all selected). */
  label: LabelEntity | null;
  labelCount: number;
  stamp: StampEntity | null;
  stampName: string;
  stampCount: number;
  labelStyles: string[];
}

/**
 * The details panel. Shows the selected map feature (with its generated facts) or the selected
 * labels/stamps, and edits them through Editor use cases (all undoable). Field edits carry a
 * coalescing key so a slider scrub is one undo step.
 */
export class InspectorViewModel extends ViewModel<InspectorState> {
  private docOff: (() => void)[] = [];

  constructor(private readonly editor: Editor) {
    super({ mode: 'none', feature: null, label: null, labelCount: 0, stamp: null, stampName: '', stampCount: 0, labelStyles: [] });
    this.track(editor.selection.events.on('change', () => this.sync()));
    this.track(editor.events.on('theme', () => this.sync()));
    const watch = () => {
      this.docOff.forEach((f) => f());
      const d = editor.doc, s = () => this.sync();
      this.docOff = [d.features.events.on('change', s), d.labels.events.on('change', s), d.stamps.events.on('change', s)];
      this.sync();
    };
    this.track(editor.events.on('doc', watch));
    this.track(() => this.docOff.forEach((f) => f()));
    watch();
  }

  private sync(): void {
    const ed = this.editor, doc = ed.doc, sel = ed.selection.state;
    const labels = [...sel.labels].map((id) => doc.labels.get(id)).filter((l): l is LabelEntity => !!l);
    const stamps = [...sel.stamps].map((id) => doc.stamps.get(id)).filter((s): s is StampEntity => !!s);
    const f = sel.feature ? doc.features.get(sel.feature) : undefined;
    const labelStyles = Object.keys(ed.theme().labels);
    if (labels.length) {
      this.set({ mode: 'labels', label: labels[0], labelCount: labels.length, feature: null, stamp: null, stampCount: 0, labelStyles });
    } else if (stamps.length) {
      const def = ed.packs.assets().get(stamps[0].asset);
      this.set({ mode: 'stamps', stamp: stamps[0], stampCount: stamps.length, stampName: def?.name ?? stamps[0].asset, feature: null, label: null, labelCount: 0, labelStyles });
    } else if (f) {
      this.set({ mode: 'feature', feature: featureView(f, !!doc.labels.get(labelIdOf(f.id)), placeHistoryOf(doc, f)), label: null, labelCount: 0, stamp: null, stampCount: 0, labelStyles });
    } else {
      this.set({ mode: 'none', feature: null, label: null, labelCount: 0, stamp: null, stampCount: 0, labelStyles });
    }
  }

  // Feature commands
  rename = (name: string): void => { const f = this.state.feature; if (f) this.editor.renameFeature(f.id, name); };
  reroll = (): void => { const f = this.state.feature; if (f) void this.editor.rerollNames([f.id]); };
  deleteFeature = (): void => { const f = this.state.feature; if (f) this.editor.deleteFeature(f.id); };
  focus = (): void => {
    const f = this.state.feature && this.editor.doc.features.get(this.state.feature.id);
    if (f) this.editor.focus(f.x, f.y, f.extent);
  };
  /** Switch to the feature's label for typographic edits. */
  editLabel = (): void => {
    const f = this.state.feature;
    if (!f) return;
    this.editor.setTool('select');
    this.editor.selection.set({ labels: new Set([labelIdOf(f.id)]), stamps: new Set(), feature: null });
  };

  // Label commands (apply to every selected label)
  updateLabel = (patch: Partial<LabelEntity>, key?: string): void => {
    this.editor.updateLabels([...this.editor.selection.state.labels], patch, key ? `label:${key}:${[...this.editor.selection.state.labels].join()}` : undefined);
  };

  // Stamp commands
  updateStamp = (patch: Partial<StampEntity>, key?: string): void => {
    this.editor.updateStamps([...this.editor.selection.state.stamps], patch, key ? `stamp:${key}:${[...this.editor.selection.state.stamps].join()}` : undefined);
  };

  deleteSelected = (): void => this.editor.deleteSelected();
  clear = (): void => this.editor.selection.clear();

}

function featureView(f: FeatureEntity, hasLabel: boolean, history: PlaceHistory | null): FeatureView {
  return { id: f.id, kind: f.kind, name: f.name, culture: f.culture, details: f.details, canReroll: !!f.request, hasLabel, history };
}

/** The story of a place from the document's history record; null when there is none to tell. */
function placeHistoryOf(doc: MapDocument, f: FeatureEntity): PlaceHistory | null {
  const h = doc.history;
  if (!h || f.tile === undefined || !PLACE_KINDS.has(f.kind)) return null;
  const rulers = rulersText(rulersOf(h, f.tile), (id) => realmName(h, id));
  const story = placeStory(h, f.tile);
  return rulers || story.length ? { rulers, story } : null;
}
