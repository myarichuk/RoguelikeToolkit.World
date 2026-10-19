import { describe, expect, it } from 'vitest';
import { MAP_H, MAP_W } from '../core/geo';
import { MapDocument } from '../doc/MapDocument';
import { DEFAULT_SETTINGS, type FeatureEntity } from '../doc/types';
import { derivePhysics, describeReport, type PhysicsEngine } from './derivePhysics';
import { unpackForTest } from './testSupport';
import type { RecomputeParams, WorldSnapshot } from './protocol';

// A fake planet: two tiles (left and right half of the map). The "engine" is flat ground whose height
// is the tile's sculpt delta, so what the editor sends is directly observable in what comes back.
const N = MAP_W * MAP_H;
const tile = new Int32Array(N);
for (let y = 0; y < MAP_H; y++) for (let x = 0; x < MAP_W; x++) tile[y * MAP_W + x] = x < MAP_W / 2 ? 0 : 1;

function fakeEngine(log: RecomputeParams[] = []): PhysicsEngine {
  return {
    async recompute(p) {
      log.push(p);
      const e = unpackForTest(p.edits);
      const elevation = new Float32Array(N), biome = new Uint8Array(N).fill(1);
      for (let i = 0; i < N; i++) {
        const t = tile[i], ed = e.get(t);
        elevation[i] = 0.3 + (ed?.dh ?? 0);
        if (ed?.pinBiome !== undefined) biome[i] = ed.pinBiome;
      }
      const lake: FeatureEntity = { id: 'f_lake:0', kind: 'lake', name: '', x: 1, y: 1, angle: 0, extent: 1, weight: 1, tiles: [0], details: [], culture: '', request: { kind: 'lake', seed: 1, race: 'h', culture: 'c', context: 0 } };
      const features = p.rastersOnly ? [] : [{ key: 'lake:0', kind: 'lake' as const, name: '', x: 1, y: 1, angle: 0, extent: 1, weight: 1, style: 0, tiles: Int32Array.from(lake.tiles!), details: [], request: lake.request }];
      return {
        seed: p.seed, size: p.size, plates: p.plates, tileCount: 2, rasters: { width: MAP_W, height: MAP_H, elevation, biome, tile, waterPixels: new Int32Array(2) }, tilePlate: new Uint32Array(2),
        plateDrift: new Float64Array(0), tileXY: new Float32Array(4), capacity: new Uint8Array(10).fill(p.edits.tiles.length + 1), seaIce: new Uint8Array(2).fill(p.edits.tiles.length + 1), climate: new Float32Array(18).fill(p.edits.tiles.length + 1), features: { features, rivers: [], roads: [] }, styles: [{ race: 'h', culture: 'c', label: 'Human' }], timings: {},
      } as WorldSnapshot;
    },
    async names(reqs) { return reqs.map((_, i) => `Name${i}`); },
  };
}

function docFor(engine: PhysicsEngine): Promise<MapDocument> {
  return engine.recompute({ seed: 1, size: 1, plates: 1, styles: [], edits: { tiles: new Int32Array(0), delta: new Float64Array(0), pinHeight: new Float64Array(0), meta: new Int32Array(0), temperature: new Float64Array(0), precipitation: new Float64Array(0) }, rastersOnly: false }).then((s) => {
    const doc = new MapDocument({ ...DEFAULT_SETTINGS, seed: 1, size: 1, plates: 1, styles: [], title: 't' }, s.rasters.elevation.slice(), s.rasters.biome.slice(),
      { tile, tilePlate: s.tilePlate, plateDrift: s.plateDrift, tileXY: s.tileXY, styles: s.styles });
    doc.features.reset([{ id: 'f_lake:0', kind: 'lake', name: 'Silverfen', x: 1, y: 1, angle: 0, extent: 1, weight: 1, tiles: [0], details: [], culture: '' }]);
    doc.physics = { edits: new Map(), baseline: { elevation: s.rasters.elevation.slice(), biome: s.rasters.biome.slice() }, generated: new Set(['f_lake:0']), tombstones: new Map() };
    return doc;
  });
}

describe('derivePhysics', () => {
  it('sends the brushed tile as a sculpt delta and returns the re-derived rasters', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.elevation[i] += 0.5;
    const out = await derivePhysics(doc, eng, 'sculpt');
    const sent = unpackForTest(log.at(-1)!.edits);
    expect(sent.get(1)!.dh).toBeCloseTo(0.5, 5);
    expect(sent.has(0)).toBe(false);
    expect(out.report.heightTiles).toBe(1);
    expect(out.elevation[N - 1]).toBeCloseTo(0.8, 5);
    expect(out.physics.edits.get(1)!.dh).toBeCloseTo(0.5, 5);
  });

  it('is idempotent: applying again with no further strokes sends the same table', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.elevation[i] += 0.5;
    const first = await derivePhysics(doc, eng, 'sculpt');
    doc.replaceRasters(first.elevation, first.biome);
    doc.physics = first.physics;
    const second = await derivePhysics(doc, eng, 'sculpt');
    expect(second.report.heightTiles).toBe(0);
    expect(unpackForTest(log.at(-1)!.edits).get(1)!.dh).toBeCloseTo(0.5, 5);
  });

  it('rebuilds the baseline from the stored table when the cache is gone (after opening a file)', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    doc.physics!.edits = new Map([[1, { dh: 0.5 }]]);
    doc.physics!.baseline = null;
    for (let i = 0; i < N; i++) doc.elevation[i] = tile[i] === 1 ? 0.8 : 0.3;          // the saved raster, nothing pending
    const out = await derivePhysics(doc, eng, 'sculpt');
    expect(log.at(-2)!.rastersOnly).toBe(true);   // the baseline rebuild precedes the real recompute
    expect(out.report.heightTiles).toBe(0);
    expect(out.physics.edits.get(1)!.dh).toBeCloseTo(0.5, 5);
  });

  it('pins painted biomes, savanna included, and leaves names alone', async () => {
    const eng = fakeEngine();
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 0) doc.biome[i] = 2;
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.biome[i] = 10;   // savanna on the other tile: a Core biome now, pinned like the rest
    const out = await derivePhysics(doc, eng, 'sculpt');
    expect(out.report.biomeTiles).toBe(2);
    expect(out.physics.edits.get(0)!.pinBiome).toBe(2);
    expect(out.physics.edits.get(1)!.pinBiome).toBe(10);
    expect(out.biome[0]).toBe(2);
    expect(out.biome[N - 1]).toBe(10);
    expect(out.features[0].name).toBe('Silverfen');
  });

  it('keeps a deleted feature deleted and names only the new ones', async () => {
    const eng = fakeEngine();
    const doc = await docFor(eng);
    const lake = doc.features.get('f_lake:0')!;
    doc.features.remove(['f_lake:0']);
    doc.physics!.tombstones.set(lake.id, lake);
    const out = await derivePhysics(doc, eng, 'sculpt');
    expect(out.features).toEqual([]);
    expect(out.physics.tombstones.has('f_lake:0')).toBe(true);
  });

  it('revert drops the whole table and the paint', async () => {
    const eng = fakeEngine();
    const doc = await docFor(eng);
    doc.physics!.edits = new Map([[1, { dh: 0.5 }]]);
    doc.biome[0] = 10;
    const out = await derivePhysics(doc, eng, 'revert');
    expect(out.physics.edits.size).toBe(0);
    expect(out.biome[0]).toBe(1);
  });

  it('sends the painted climate as per-tile offsets, keeps the paint, and a revert clears it', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1) { doc.warmth[i] = 127; doc.wetness[i] = -127; }
    const out = await derivePhysics(doc, eng, 'sculpt');
    const sent = unpackForTest(log.at(-1)!.edits);
    expect(sent.get(1)!.dt).toBeCloseTo(0.4, 5);
    expect(sent.get(1)!.dr).toBeCloseTo(-0.4, 5);
    expect(sent.has(0)).toBe(false);
    expect(out.report.climateTiles).toBe(1);
    expect(out.warmth[N - 1]).toBe(127);
    expect(describeReport(out.report)).toContain('1 tiles re-climated');
    const again = await derivePhysics(doc, eng, 'revert');
    expect(again.warmth.every((v) => v === 0) && again.wetness.every((v) => v === 0)).toBe(true);
    expect(again.physics.edits.size).toBe(0);
    expect(doc.warmth[N - 1]).toBe(127);              // derivePhysics does not touch the document; the command swaps frames
  });

  it('sends painted land use as per-tile pins, never for water, and erasing it removes the pin', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1 && doc.biome[i] !== 0) doc.landuse[i] = 3;
    const out = await derivePhysics(doc, eng, 'sculpt');
    const sent = unpackForTest(log.at(-1)!.edits);
    expect(sent.get(1)?.lu).toBe(3);
    expect(sent.get(1)?.pinBiome).toBeUndefined();     // a land-use pin is not a biome pin
    expect(out.report.landUseTiles).toBe(1);
    expect(describeReport(out.report)).toContain('1 land-use pins');
    doc.replaceRasters(out.elevation, out.biome);
    doc.physics = out.physics;
    doc.landuse.fill(0);
    const again = await derivePhysics(doc, eng, 'sculpt');
    expect(again.physics.edits.has(1)).toBe(false);
  });

  it('erasing the paint and applying again removes the offset from the table', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.warmth[i] = 127;
    const first = await derivePhysics(doc, eng, 'sculpt');
    doc.replaceRasters(first.elevation, first.biome);
    doc.physics = first.physics;
    doc.warmth.fill(0);
    const second = await derivePhysics(doc, eng, 'sculpt');
    expect(second.physics.edits.has(1)).toBe(false);
    expect(unpackForTest(log.at(-1)!.edits).size).toBe(0);
  });

  it('asks the engine for the world the document was generated on, planet included', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    doc.setSettings({ obliquity: 60, flux: 0.95, rotation: 1.5 });
    await derivePhysics(doc, eng, 'sculpt');
    expect(log.at(-1)!.planet).toEqual({ obliquity: 60, flux: 0.95, rotation: 1.5 });
  });

  it('asks for the climate model the document was generated with', async () => {
    const log: RecomputeParams[] = [];
    const eng = fakeEngine(log);
    const doc = await docFor(eng);
    doc.setSettings({ climateRefresh: false, meridionalWind: 0, windCellBlend: 0.75, oceanHeat: 0.5, oceanRain: 1, aeolian: 0 });
    await derivePhysics(doc, eng, 'sculpt');
    expect(log.at(-1)!.model).toEqual({ climateRefresh: false, meridionalWind: 0, windCellBlend: 0.75, oceanHeat: 0.5, oceanRain: 1, aeolian: 0 });
  });

  it('carries the engine\'s land capacity into the physics state, refreshed on every apply', async () => {
    const eng = fakeEngine();
    const doc = await docFor(eng);
    expect(doc.physics!.capacity).toBeUndefined();               // the fake baseline has none; a real generate supplies it
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.warmth[i] = 127;
    const out = await derivePhysics(doc, eng, 'sculpt');
    expect(out.physics.capacity).toBeDefined();
    expect(out.physics.capacity![0]).toBe(2);                    // one edited tile, so the fake engine said 1 + 1
  });

  it('carries the engine\'s sea ice into the physics state, so a brushed cold patch can freeze the sea', async () => {
    const eng = fakeEngine();
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.warmth[i] = -127;
    const out = await derivePhysics(doc, eng, 'sculpt');
    expect(out.physics.seaIce).toBeDefined();
    expect(out.physics.seaIce![0]).toBe(2);
  });

  it('carries the engine\'s climate into the physics state too, so the overlay follows an apply', async () => {
    const eng = fakeEngine();
    const doc = await docFor(eng);
    for (let i = 0; i < N; i++) if (tile[i] === 1) doc.warmth[i] = 127;
    const out = await derivePhysics(doc, eng, 'sculpt');
    expect(out.physics.climate).toBeDefined();
    expect(out.physics.climate![0]).toBe(2);
  });

  it('refuses a hand-made map', async () => {
    await expect(derivePhysics(MapDocument.empty(), fakeEngine(), 'sculpt')).rejects.toThrow(/no generated world/);
  });

  it('describes what moved', () => {
    expect(describeReport({ heightTiles: 12, biomeTiles: 0, climateTiles: 0, landUseTiles: 0, counts: { river: [14, 17], lake: [9, 9] }, kept: 20, added: 3, removed: 0 })).toBe('12 tiles reshaped · rivers 14→17 · 3 new');
    expect(describeReport({ heightTiles: 0, biomeTiles: 0, climateTiles: 0, landUseTiles: 0, counts: {}, kept: 0, added: 0, removed: 0 })).toBe('no change');
  });
});
