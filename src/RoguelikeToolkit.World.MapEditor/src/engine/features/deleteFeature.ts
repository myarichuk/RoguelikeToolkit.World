import type { Command } from '../commands/Command';
import { CompositeCommand } from '../commands/Command';
import { RemoveEntities } from '../commands/EntityCommands';
import type { MapDocument } from '../doc/MapDocument';

export interface FeatureDeletion {
  command: Command;
  /** What went away besides the feature record itself (label, river ribbon, settlement marker). */
  removed: string[];
}

/**
 * Deleting a feature removes its record and everything drawn *for* it: the auto label, the river
 * ribbon, the settlement marker. Terrain (a sea's water, a forest's trees) is not owned by the
 * feature and stays; `removed` lets the caller say so instead of appearing to do nothing.
 */
export function deleteFeatureCommand(doc: MapDocument, id: string): FeatureDeletion | null {
  const f = doc.features.get(id);
  if (!f) return null;
  const parts: Command[] = [new RemoveEntities('feature', doc.features, [id])];
  const removed: string[] = [];
  if (doc.labels.get(`lb_${id}`)) { parts.push(new RemoveEntities('label', doc.labels, [`lb_${id}`])); removed.push('label'); }
  if (f.riverId && doc.rivers.get(f.riverId)) { parts.push(new RemoveEntities('river', doc.rivers, [f.riverId])); removed.push('river'); }
  if (doc.stamps.get(`st_${id}`)) { parts.push(new RemoveEntities('marker', doc.stamps, [`st_${id}`])); removed.push('marker'); }
  return { command: new CompositeCommand(`Delete ${f.name || f.kind}`, parts), removed };
}
