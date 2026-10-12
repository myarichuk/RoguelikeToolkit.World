// The "Apply physics" use case, minus the editor: turn the user's pixel edits into a tile edit
// table, have the engine re-derive the world from it, and carry the document's identities
// (names, deletions, hand-made features, editor-only paint) across. Pure of rendering and UI, so
// it is tested against a fake engine.
import { entitiesFromSnapshot } from '../doc/fromWorld';
import type { PhysicsState } from '../doc/MapDocument';
import { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, RiverEntity } from '../doc/types';
import { collectClimate, collectEdits, collectLandUse, EDITOR_ONLY_BIOMES, packEdits, type EditMode, type Raster } from './edits';
import type { NameRequest, RecomputeParams, WorldSnapshot } from './protocol';
import { reconcileFeatures } from './reconcile';

export type PhysicsMode = EditMode | 'revert';

export interface PhysicsEngine {
  recompute(params: RecomputeParams): Promise<WorldSnapshot>;
  names(requests: NameRequest[]): Promise<string[]>;
}

export interface PhysicsReport {
  /** Tiles whose height edit was taken from the brush work. */
  heightTiles: number;
  /** Tiles pinned to a painted biome. */
  biomeTiles: number;
  /** Tiles carrying a brushed temperature or rainfall offset. */
  climateTiles: number;
  /** Tiles pinned to a painted land use. */
  landUseTiles: number;
  /** Engine features per kind: [before, after]. */
  counts: Record<string, [number, number]>;
  kept: number; added: number; removed: number;
}

export interface PhysicsOutcome {
  elevation: Float32Array;
  biome: Uint8Array;
  /** The engine-owned features and their river ribbons, reconciled; hand-made ones are not in here. */
  features: FeatureEntity[];
  rivers: RiverEntity[];
  physics: PhysicsState;
  /** The climate paint after the apply: the same as before, or all zero after a revert. */
  warmth: Int8Array;
  wetness: Int8Array;
  report: PhysicsReport;
}

export async function derivePhysics(doc: MapDocument, engine: PhysicsEngine, mode: PhysicsMode): Promise<PhysicsOutcome> {
  const ph = doc.physics;
  if (!ph || !doc.hasWorld) throw new Error('This map has no generated world behind it, so there is nothing to re-derive. Generate a world first.');
  const { seed, size, plates, styles, obliquity, flux, rotation, climateRefresh, meridionalWind, oceanHeat, oceanRain, aeolian } = doc.settings;
  const params = { seed, size, plates, styles, planet: { obliquity, flux, rotation }, model: { climateRefresh, meridionalWind, oceanHeat, oceanRain, aeolian } };
  const tileCount = doc.world.tileXY.length / 2;

  let edits = ph.edits, heightTiles = 0, biomeTiles = 0, climateTiles = 0, landUseTiles = 0;
  let warmth = doc.warmth, wetness = doc.wetness;
  if (mode === 'revert') {
    edits = new Map();
    warmth = new Int8Array(warmth.length); wetness = new Int8Array(wetness.length);
  } else {
    // What the engine last produced for the stored table; deterministic, so rebuilt on demand (after open, say).
    const baseline: Raster = ph.baseline ?? (await engine.recompute({ ...params, edits: packEdits(ph.edits), rastersOnly: true })).rasters;
    const r = collectEdits({ elevation: doc.elevation, biome: doc.biome }, baseline, doc.world.tile, tileCount, ph.edits, mode);
    // The climate paint is an input the author keeps editing, so it is re-read whole every time, not diffed.
    const c = collectClimate(doc.warmth, doc.wetness, doc.world.tile, tileCount, r.edits);
    // Likewise the land-use paint: it becomes pins the engine's plugins must respect.
    const l = collectLandUse(doc.landuse, doc.biome, doc.world.tile, tileCount, c.edits);
    edits = l.edits; landUseTiles = l.landUseTiles; heightTiles = r.heightTiles; biomeTiles = r.biomeTiles; climateTiles = c.climateTiles;
  }

  const snap = await engine.recompute({ ...params, edits: packEdits(edits), rastersOnly: false });
  const baseline: Raster = { elevation: snap.rasters.elevation.slice(), biome: snap.rasters.biome.slice() };

  // Savanna is paint, not climate: the world cannot reproduce them, so they stay where they were drawn.
  const biome = snap.rasters.biome;
  if (mode !== 'revert') for (let i = 0; i < biome.length; i++) if (EDITOR_ONLY_BIOMES.has(doc.biome[i])) biome[i] = doc.biome[i];

  const live = [...doc.features.all()].filter((f) => ph.generated.has(f.id));
  const next = entitiesFromSnapshot(snap);
  const rec = reconcileFeatures({
    live, rivers: new Map([...doc.rivers.all()].map((r) => [r.id, r])),
    tombstones: [...ph.tombstones.values()].filter((t) => !doc.features.has(t.id)), next,
  });

  if (rec.pending.length) {
    const byId = new Map(rec.features.map((f) => [f.id, f]));
    const names = await engine.names(rec.pending.map((id) => byId.get(id)!.request!));
    rec.pending.forEach((id, i) => { byId.get(id)!.name = names[i]; });
    const riverOf = new Map(rec.rivers.map((r) => [r.id, r]));
    for (const f of rec.features) if (f.riverId && riverOf.has(f.riverId)) riverOf.get(f.riverId)!.name = f.name;
  }

  const counts: Record<string, [number, number]> = {};
  for (const f of live) (counts[f.kind] ??= [0, 0])[0]++;
  for (const f of rec.features) (counts[f.kind] ??= [0, 0])[1]++;

  return {
    elevation: snap.rasters.elevation, biome,
    features: rec.features, rivers: rec.rivers,
    warmth: warmth.slice(), wetness: wetness.slice(),
    physics: { edits, baseline, generated: new Set(rec.features.map((f) => f.id)), tombstones: new Map(rec.tombstones.map((t) => [t.id, t])), capacity: snap.capacity, climate: snap.climate },
    report: { heightTiles, biomeTiles, climateTiles, landUseTiles, counts, kept: rec.kept, added: rec.added, removed: rec.removed },
  };
}

/** One line for the status bar: what the edit table did and what moved in the world. */
export function describeReport(r: PhysicsReport): string {
  const bits: string[] = [];
  if (r.heightTiles) bits.push(`${r.heightTiles} tiles reshaped`);
  if (r.biomeTiles) bits.push(`${r.biomeTiles} repainted`);
  if (r.climateTiles) bits.push(`${r.climateTiles} tiles re-climated`);
  if (r.landUseTiles) bits.push(`${r.landUseTiles} land-use pins`);
  const moved = Object.entries(r.counts).filter(([, [a, b]]) => a !== b).map(([k, [a, b]]) => `${k}s ${a}→${b}`);
  if (moved.length) bits.push(moved.join(', '));
  if (r.added) bits.push(`${r.added} new`);
  if (r.removed) bits.push(`${r.removed} gone`);
  return bits.length ? bits.join(' · ') : 'no change';
}
