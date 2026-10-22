import { newId } from '../core/ids';
import type { WorldSnapshot } from '../world/protocol';
import { isGeneratedFeatureId, MapDocument } from './MapDocument';
import { DEFAULT_SETTINGS, type DocSettings, type FeatureEntity, type RiverEntity } from './types';

/** The editable entities of a snapshot's extracted features: river ribbons plus one entity per feature. */
export function entitiesFromSnapshot(w: WorldSnapshot): { features: FeatureEntity[]; rivers: RiverEntity[] } {
  const rivers: RiverEntity[] = [];
  const riverIds = w.features.rivers.map((r) => {
    const id = newId('river');
    rivers.push({ id, name: '', points: Array.from(r.points, (v) => Math.round(v * 100) / 100) });
    return id;
  });
  const features: FeatureEntity[] = w.features.features.map((f) => {
    const riverId = f.river !== undefined ? riverIds[f.river] : undefined;
    if (riverId) rivers[f.river!].name = f.name;
    return {
      id: `f_${f.key}`, kind: f.kind, name: f.name, x: f.x, y: f.y, angle: f.angle, extent: f.extent, weight: f.weight, tile: f.tile, span: f.span,
      tiles: f.tiles ? Array.from(f.tiles) : undefined, riverId, deposit: f.deposit, details: f.details,
      culture: w.styles[f.style]?.label ?? '', request: f.request,
    };
  });
  return { features, rivers };
}

/** Factory: a fresh document from a generated world (rasters, rivers, features; no stamps/labels yet). */
export function documentFromWorld(w: WorldSnapshot, settings: Partial<DocSettings> = {}): MapDocument {
  const doc = new MapDocument(
    { ...DEFAULT_SETTINGS, ...settings, seed: w.seed, size: w.size, plates: w.plates },
    w.rasters.elevation, w.rasters.biome,
    { tile: w.rasters.tile, tilePlate: w.tilePlate, tileStyle: w.tileStyle, plateDrift: w.plateDrift, tileXY: w.tileXY, styles: w.styles },
  );
  const { features, rivers } = entitiesFromSnapshot(w);
  doc.rivers.reset(rivers);
  doc.roads.reset(w.features.roads.map((r) => ({
    id: newId('road'), kind: r.kind, keeper: r.keeper, severed: r.severed, founded: r.founded, points: Array.from(r.points, (v) => Math.round(v * 100) / 100),
  })));
  doc.features.reset(features);
  doc.history = w.history;
  // The pristine world is the baseline brush strokes are measured against; every feature here is the engine's.
  doc.physics = {
    edits: new Map(),
    baseline: { elevation: w.rasters.elevation.slice(), biome: w.rasters.biome.slice() },
    generated: new Set(features.map((f) => f.id).filter(isGeneratedFeatureId)),
    tombstones: new Map(),
    capacity: w.capacity,
    seaIce: w.seaIce,
    climate: w.climate,
  };
  return doc;
}
