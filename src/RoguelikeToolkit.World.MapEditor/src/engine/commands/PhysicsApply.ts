import type { MapDocument, PhysicsState } from '../doc/MapDocument';
import type { FeatureEntity, LabelEntity, RiverEntity, StampEntity } from '../doc/types';
import type { Command } from './Command';

/** Everything "Apply physics" can change, as a value: swapping two frames is the whole command. */
export interface PhysicsFrame {
  elevation: Float32Array;
  biome: Uint8Array;
  features: FeatureEntity[];
  rivers: RiverEntity[];
  labels: LabelEntity[];
  stamps: StampEntity[];
  physics: PhysicsState | null;
  /** The climate paint: cleared by a revert, so an undo of it has to put it back. */
  warmth: Int8Array;
  wetness: Int8Array;
}

export function captureFrame(doc: MapDocument): PhysicsFrame {
  const p = doc.physics;
  return {
    elevation: doc.elevation.slice(), biome: doc.biome.slice(), warmth: doc.warmth.slice(), wetness: doc.wetness.slice(),
    features: doc.features.toArray(), rivers: doc.rivers.toArray(), labels: doc.labels.toArray(), stamps: doc.stamps.toArray(),
    physics: p && { edits: new Map(p.edits), baseline: p.baseline, generated: new Set(p.generated), tombstones: new Map(p.tombstones) },
  };
}

export function applyFrame(doc: MapDocument, f: PhysicsFrame): void {
  doc.replaceRasters(f.elevation, f.biome);
  doc.replaceClimate(f.warmth, f.wetness);
  doc.rivers.reset(f.rivers);
  doc.features.reset(f.features);
  doc.labels.reset(f.labels);
  doc.stamps.reset(f.stamps);
  doc.physics = f.physics;
}

/**
 * One undo step for a physics re-derivation: terrain, biomes, features, rivers, labels, stamps and
 * the edit table swap together, so undo returns to exactly the document the author had, brush
 * strokes included. (The engine keeps no state of its own: the next apply sends the table again.)
 */
export class PhysicsApply implements Command {
  constructor(readonly label: string, private readonly doc: MapDocument, private readonly before: PhysicsFrame, private readonly after: PhysicsFrame) {}
  do(): void { applyFrame(this.doc, this.after); }
  undo(): void { applyFrame(this.doc, this.before); }
}
