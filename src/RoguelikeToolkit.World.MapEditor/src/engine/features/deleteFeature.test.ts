import { describe, expect, it, vi } from 'vitest';
import { UndoStack } from '../commands/UndoStack';
import { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, LabelEntity, RiverEntity, StampEntity } from '../doc/types';
import { InspectTool } from '../tools/InspectTool';
import { SelectTool } from '../tools/SelectTool';
import { Selection } from '../tools/Selection';
import type { ToolContext } from '../tools/Tool';
import { deleteFeatureCommand } from './deleteFeature';

const feature = (id: string, kind: FeatureEntity['kind'], extra: Partial<FeatureEntity> = {}): FeatureEntity => ({
  id, kind, name: id, x: 10, y: 10, angle: 0, extent: 5, weight: 1, details: [], culture: '', ...extra,
});
const label = (id: string): LabelEntity => ({ id, text: id, style: 'sea', x: 0, y: 0, angle: 0, size: 0, spacing: NaN, curve: 0 });
const stamp = (id: string): StampEntity => ({ id, asset: 'core.city', variant: 0, x: 0, y: 0, scale: 1, rotation: 0, flip: false });
const river = (id: string): RiverEntity => ({ id, name: id, points: [0, 0, 1, 5, 5, 1] });

function docWith(): MapDocument {
  const doc = MapDocument.empty();
  doc.features.add([
    feature('f_sea:0:0', 'sea'),
    feature('f_river:1', 'river', { riverId: 'r1' }),
    feature('f_settlement:9', 'town'),
    feature('f_forest:3', 'forest'),
  ]);
  doc.labels.add([label('lb_f_sea:0:0'), label('lb_f_river:1'), label('lb_f_settlement:9'), label('hand-made')]);
  doc.rivers.add([river('r1')]);
  doc.stamps.add([stamp('st_f_settlement:9'), stamp('st_tree_1_1')]);
  return doc;
}

describe('deleteFeatureCommand', () => {
  it('removes a sea and its label, and undo restores both', () => {
    const doc = docWith(), undo = new UndoStack();
    const del = deleteFeatureCommand(doc, 'f_sea:0:0')!;
    undo.execute(del.command);
    expect(doc.features.has('f_sea:0:0')).toBe(false);
    expect(doc.labels.has('lb_f_sea:0:0')).toBe(false);
    expect(del.removed).toEqual(['label']);
    undo.undo();
    expect(doc.features.has('f_sea:0:0')).toBe(true);
    expect(doc.labels.has('lb_f_sea:0:0')).toBe(true);
  });

  it('takes the river ribbon with a river and the marker with a settlement', () => {
    const doc = docWith();
    deleteFeatureCommand(doc, 'f_river:1')!.command.do();
    expect(doc.rivers.has('r1')).toBe(false);
    const town = deleteFeatureCommand(doc, 'f_settlement:9')!;
    town.command.do();
    expect(town.removed).toEqual(['label', 'marker']);
    expect(doc.stamps.has('st_f_settlement:9')).toBe(false);
    // Hand-placed and rule-scattered things are not the feature's.
    expect(doc.labels.has('hand-made')).toBe(true);
    expect(doc.stamps.has('st_tree_1_1')).toBe(true);
  });

  it('reports a feature with nothing drawn for it, so the caller can say the terrain stays', () => {
    const del = deleteFeatureCommand(docWith(), 'f_forest:3')!;
    expect(del.removed).toEqual([]);
    del.command.do();
  });

  it('is null for an unknown id', () => {
    expect(deleteFeatureCommand(docWith(), 'nope')).toBeNull();
  });
});

describe('Delete key', () => {
  const key = (k: string) => ({ key: k }) as KeyboardEvent;
  function ctx(): ToolContext & { deleteSelected: ReturnType<typeof vi.fn> } {
    return { selection: new Selection(), deleteSelected: vi.fn(), doc: MapDocument.empty() } as unknown as ToolContext & { deleteSelected: ReturnType<typeof vi.fn> };
  }

  it('deletes the feature selected with the Inspect tool (it used to be swallowed)', () => {
    const c = ctx();
    c.selection.set({ feature: 'f_sea:0:0' });
    for (const k of ['Delete', 'Backspace']) expect(new InspectTool().key(key(k), c)).toBe(true);
    expect(c.deleteSelected).toHaveBeenCalledTimes(2);
  });

  it('leaves the key alone when nothing is inspected', () => {
    const c = ctx();
    expect(new InspectTool().key(key('Delete'), c)).toBe(false);
    expect(c.deleteSelected).not.toHaveBeenCalled();
  });

  it('Select tool deletes a still-selected feature when no stamp or label is selected', () => {
    const c = ctx();
    c.selection.set({ feature: 'f_sea:0:0' });
    expect(new SelectTool().key(key('Delete'), c)).toBe(true);
    expect(c.deleteSelected).toHaveBeenCalledTimes(1);
  });

  it('Select tool still routes stamp/label deletion through the same use case', () => {
    const c = ctx();
    c.doc.stamps.add([stamp('s1')]);
    c.selection.set({ stamps: new Set(['s1']) });
    expect(new SelectTool().key(key('Delete'), c)).toBe(true);
    expect(c.deleteSelected).toHaveBeenCalledTimes(1);
  });
});
