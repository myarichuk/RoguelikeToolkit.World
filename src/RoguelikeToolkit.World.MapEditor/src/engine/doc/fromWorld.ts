import { newId } from '../core/ids';
import type { WorldSnapshot } from '../world/protocol';
import { MapDocument } from './MapDocument';
import { DEFAULT_SETTINGS, type DocSettings, type FeatureEntity, type RiverEntity } from './types';

/** Factory: a fresh document from a generated world (rasters, rivers, features; no stamps/labels yet). */
export function documentFromWorld(w: WorldSnapshot, settings: Partial<DocSettings> = {}): MapDocument {
  const doc = new MapDocument(
    { ...DEFAULT_SETTINGS, ...settings, seed: w.seed, size: w.size, plates: w.plates },
    w.rasters.elevation, w.rasters.biome,
    { tile: w.rasters.tile, tilePlate: w.tilePlate, plateDrift: w.plateDrift, tileXY: w.tileXY, styles: w.styles },
  );
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
      id: `f_${f.key}`, kind: f.kind, name: f.name, x: f.x, y: f.y, angle: f.angle, extent: f.extent, weight: f.weight,
      tiles: f.tiles ? Array.from(f.tiles) : undefined, riverId, deposit: f.deposit, details: f.details,
      culture: w.styles[f.style]?.label ?? '', request: f.request,
    };
  });
  doc.rivers.reset(rivers);
  doc.features.reset(features);
  return doc;
}
