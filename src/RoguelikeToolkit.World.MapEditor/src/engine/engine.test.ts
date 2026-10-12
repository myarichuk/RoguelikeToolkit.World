import { describe, expect, it } from 'vitest';
import { CompositeCommand } from './commands/Command';
import { AddEntities, UpdateEntities } from './commands/EntityCommands';
import { BLOCK, RasterEdit } from './commands/RasterEdit';
import { UndoStack } from './commands/UndoStack';
import corePack from './content/core-pack.json';
import { PackRegistry } from './content/PackRegistry';
import { validatePack } from './content/validate';
import { MAP_H, MAP_W, latOfY, lonLatToMap, lonOfX, vecToMap } from './core/geo';
import { MapDocument } from './doc/MapDocument';
import { DEFAULT_SETTINGS } from './doc/types';
import { decodeDocument, encodeDocument } from './doc/serialize';
import type { LabelEntity, StampEntity } from './doc/types';
import { contour, pathData, shape } from './export/contour';
import { riverPath } from './export/SvgExporter';
import { climateDab, heightDab, strokePoints } from './tools/brushes';
import { BIOME_NAMES } from './world/features';

describe('Mercator map space', () => {
  it('round-trips lon/lat through map units', () => {
    for (const [lon, lat] of [[0, 0], [1.2, 0.7], [-2.9, -1.1], [3.0, 1.3]]) {
      const p = lonLatToMap(lon, lat);
      expect(lonOfX(p.x)).toBeCloseTo(lon, 9);
      expect(latOfY(p.y)).toBeCloseTo(lat, 9);
    }
  });
  it('is conformal: the map is square in angle (MAP_H covers ±MAX_LAT symmetrically)', () => {
    expect(lonLatToMap(0, 0).y).toBeCloseTo(MAP_H / 2, 9);
    expect(lonLatToMap(0, 2).y).toBeCloseTo(0, 6); // clamped at the cut
    const v = vecToMap(1, 0, 0);
    expect(v.x).toBeCloseTo(MAP_W / 2, 9);
  });
});

describe('RasterEdit + UndoStack', () => {
  it('undoes and redoes a stroke exactly, touching only the blocks it wrote', () => {
    const W = 256, H = 128, data = new Float32Array(W * H).fill(-0.2);
    const touched: unknown[] = [];
    const target = { data, width: W, height: H, touched: (r: unknown) => touched.push(r) };
    const original = data.slice();
    const edit = new RasterEdit('stroke', target);
    const dab = { x: 250, y: 60, radius: 12, strength: 0.5 };
    edit.prepare({ x0: dab.x - dab.radius, y0: dab.y - dab.radius, x1: dab.x + dab.radius, y1: dab.y + dab.radius });
    heightDab(data, W, H, dab, 'raise');
    expect(edit.commit()).toBe(true);
    // Crosses the x seam: blocks at both ends, nothing in between.
    expect(edit.blockCount).toBeLessThanOrEqual(4);
    const after = data.slice();
    const undo = new UndoStack();
    undo.record(edit);
    undo.undo();
    expect(data).toEqual(original);
    undo.redo();
    expect(data).toEqual(after);
    expect(BLOCK).toBe(64);
  });

  it('coalesces keyed entity updates into one undo step', () => {
    const doc = MapDocument.empty();
    const stamp: StampEntity = { id: 's1', asset: 'core.hill', variant: 0, x: 10, y: 10, scale: 1, rotation: 0, flip: false };
    const undo = new UndoStack();
    undo.execute(new AddEntities('add', doc.stamps, [stamp]));
    for (const scale of [1.1, 1.2, 1.3]) {
      const cur = doc.stamps.get('s1')!;
      undo.execute(new UpdateEntities('scale', doc.stamps, [cur], [{ ...cur, scale }], 'scale:s1'));
    }
    expect(doc.stamps.get('s1')!.scale).toBe(1.3);
    undo.undo();
    expect(doc.stamps.get('s1')!.scale).toBe(1);
    undo.undo();
    expect(doc.stamps.size).toBe(0);
    expect(undo.state.canUndo).toBe(false);
  });

  it('runs composites in order and undoes them in reverse', () => {
    const log: string[] = [];
    const c = (n: string) => ({ label: n, do: () => log.push(`do ${n}`), undo: () => log.push(`undo ${n}`) });
    const comp = new CompositeCommand('both', [c('a'), c('b')]);
    comp.do(); comp.undo();
    expect(log).toEqual(['do a', 'do b', 'undo b', 'undo a']);
  });
});

describe('brushes', () => {
  it('spaces dabs evenly along a stroke and carries the remainder', () => {
    const a = strokePoints(0, 0, 10, 0, 4, 0);
    expect(a.pts.map((p) => p[0])).toEqual([4, 8]);
    const b = strokePoints(10, 0, 13, 0, 4, a.carry);
    expect(b.pts.map((p) => p[0])).toEqual([12]);
  });
  it('paints land above sea level and wraps across the seam', () => {
    const W = 128, H = 64, data = new Float32Array(W * H).fill(-0.3);
    heightDab(data, W, H, { x: 1, y: 32, radius: 6, strength: 1 }, 'land');
    expect(data[32 * W + 1]).toBeGreaterThan(0);
    expect(data[32 * W + (W - 2)]).toBeGreaterThan(0);
    expect(data[32 * W + 64]).toBe(Math.fround(-0.3));
  });
});

describe('climate brush', () => {
  const W = 64, H = 64;
  it('raises and lowers the paint with a falloff, clamped to a signed byte', () => {
    const data = new Int8Array(W * H);
    for (let k = 0; k < 400; k++) climateDab(data, W, H, { x: 32, y: 32, radius: 10, strength: 0.2 }, 'raise');
    expect(data[32 * W + 32]).toBe(127);
    expect(data[32 * W + 38]).toBeGreaterThan(0);          // inside the brush, off center
    expect(data[32 * W + 45]).toBe(0);                     // outside the brush
    for (let k = 0; k < 800; k++) climateDab(data, W, H, { x: 32, y: 32, radius: 10, strength: 0.2 }, 'lower');
    expect(data[32 * W + 32]).toBe(-127);
  });
  it('never rounds a light touch away', () => {
    const data = new Int8Array(W * H);
    climateDab(data, W, H, { x: 32, y: 32, radius: 10, strength: 0.0005 }, 'raise');
    expect(data[32 * W + 32]).toBeGreaterThanOrEqual(1);
  });
  it('erase fades the paint back to zero without overshooting', () => {
    const data = new Int8Array(W * H).fill(-100);
    for (let k = 0; k < 200; k++) climateDab(data, W, H, { x: 32, y: 32, radius: 10, strength: 0.1 }, 'erase');
    expect(data[32 * W + 32]).toBe(0);
    expect(data[0]).toBe(-100);
  });
  it('is one undo step per stroke on the paint raster', () => {
    const doc = MapDocument.empty();
    const edit = new RasterEdit('Climate', doc.target('warmth'));
    edit.prepare({ x0: 92, y0: 92, x1: 109, y1: 109 });      // the snapshot comes first, as in a real stroke
    const rect = climateDab(doc.warmth, doc.width, doc.height, { x: 100, y: 100, radius: 8, strength: 0.1 }, 'raise');
    doc.target('warmth').touched(rect);
    expect(edit.commit()).toBe(true);
    expect(doc.warmth[100 * doc.width + 100]).toBeGreaterThan(0);
    edit.undo();
    expect(doc.hasClimatePaint).toBe(false);
  });
});

describe('content packs', () => {
  it('accepts the core pack and the example pack', async () => {
    expect(validatePack(corePack).errors).toEqual([]);
    const example = (await import('../../public/packs/example-pack.json')).default;
    expect(validatePack(example).errors).toEqual([]);
  });
  it('reports every problem with a path', () => {
    const { pack, errors } = validatePack({ id: 'x', name: 'X', version: '1', assets: [{ id: 'a', name: 'A', category: 'c', size: 'big' }], biomes: [{ id: 300, name: 'B', vivid: 'red' }] });
    expect(pack).toBeUndefined();
    expect(errors).toEqual(expect.arrayContaining([
      'assets[0].size: expected a number', 'assets[0]: needs either "generator" or "svg"',
      'biomes[0].vivid: expected a color', 'biomes[0].id: must be an integer 0..255',
    ]));
  });
  it('layers packs by id; the user overlay overrides and resets non-destructively', () => {
    const reg = new PackRegistry();
    reg.add(corePack, 'http://x/');
    const base = reg.theme('parchment');
    (globalThis as { location?: unknown }).location ??= { href: 'http://x/' };
    reg.putTheme({ ...base, map: { ...base.map, sea: '#123456' } });
    expect(reg.theme('parchment').map.sea).toBe('#123456');
    expect(reg.isEdited('parchment')).toBe(true);
    reg.resetTheme('parchment');
    expect(reg.theme('parchment').map.sea).toBe(base.map.sea);
    expect(reg.fonts().serif).toBe('http://x/fonts/IMFellEnglish-Regular.woff');
  });
});

// toEqual on 3M-element typed arrays is a deep walk (seconds); compare bytes instead.
const sameBytes = (a: ArrayBufferView, b: ArrayBufferView) => {
  const x = new Uint8Array(a.buffer, a.byteOffset, a.byteLength), y = new Uint8Array(b.buffer, b.byteOffset, b.byteLength);
  if (x.length !== y.length) return false;
  for (let i = 0; i < x.length; i++) if (x[i] !== y[i]) return false;
  return true;
};

describe('.wmap serialization', () => {
  it('round-trips rasters, entities and NaN label spacing', () => {
    const doc = MapDocument.empty();
    doc.elevation[12345] = 0.42; doc.biome[777] = 12;
    doc.setSettings({ title: 'Test', seed: 99 });
    const label: LabelEntity = { id: 'l1', text: 'Ælfheim', style: 'city', x: 1, y: 2, angle: 0, size: 0, spacing: NaN, curve: 0.2, path: [1, 2, 3, 4] };
    doc.labels.add([label, { ...label, id: 'l2', spacing: 0.1 }]);
    doc.stamps.add([{ id: 's', asset: 'core.hill', variant: 2, x: 5, y: 6, scale: 1.5, rotation: 0.3, flip: true }]);
    const back = decodeDocument(encodeDocument(doc));
    expect(back.settings.title).toBe('Test');
    expect(sameBytes(back.elevation, doc.elevation)).toBe(true);
    expect(sameBytes(back.biome, doc.biome)).toBe(true);
    expect(Number.isNaN(back.labels.get('l1')!.spacing)).toBe(true);
    expect(back.labels.get('l2')!.spacing).toBe(0.1);
    expect(back.labels.get('l1')!.text).toBe('Ælfheim');
    expect(back.stamps.get('s')).toEqual(doc.stamps.get('s'));
  });
  it('round-trips the land-capacity cache, and drops one that does not fit the world', () => {
    const world = (tiles: number) => ({ tile: new Int32Array(MAP_W * MAP_H).fill(-1), tilePlate: new Uint32Array(tiles), plateDrift: new Float64Array(0), tileXY: new Float32Array(tiles * 2), styles: [] });
    const make = (tiles: number, capacity: Uint8Array | undefined) => {
      const doc = new MapDocument({ ...DEFAULT_SETTINGS }, new Float32Array(MAP_W * MAP_H), new Uint8Array(MAP_W * MAP_H), world(tiles));
      doc.physics = { edits: new Map(), baseline: null, generated: new Set(), tombstones: new Map(), capacity };
      return doc;
    };
    const cap = Uint8Array.from({ length: 15 }, (_, i) => i * 10);
    expect([...decodeDocument(encodeDocument(make(3, cap))).physics!.capacity!]).toEqual([...cap]);
    expect(decodeDocument(encodeDocument(make(3, undefined))).physics!.capacity).toBeUndefined();
    // Five bytes per tile: a cache for a different tile count must not be trusted.
    expect(decodeDocument(encodeDocument(make(3, new Uint8Array(10)))).physics!.capacity).toBeUndefined();
  });
  it('round-trips the planet, and an older file without one reads as Earth', () => {
    const doc = MapDocument.empty();
    doc.setSettings({ obliquity: 71, flux: 0.9, rotation: 0.5 });
    const back = decodeDocument(encodeDocument(doc));
    expect([back.settings.obliquity, back.settings.flux, back.settings.rotation]).toEqual([71, 0.9, 0.5]);
    const old = MapDocument.empty();
    for (const k of ['obliquity', 'flux', 'rotation'] as const) delete (old.settings as Partial<typeof old.settings>)[k];
    const reread = decodeDocument(encodeDocument(old));
    expect([reread.settings.obliquity, reread.settings.flux, reread.settings.rotation]).toEqual([23.44, 1, 1]);
  });
  it('round-trips the hand-edit table, the generated-feature ids and the tombstones', () => {
    const doc = MapDocument.empty();
    const gone = { id: 'f_lake:4', kind: 'lake' as const, name: 'Gone', x: 1, y: 2, angle: 0, extent: 1, weight: 1, tiles: [1, 2], details: [] as [string, string][], culture: '' };
    doc.physics = { edits: new Map([[3, { dh: 0.25 }], [9, { pinH: 0.5, pinBiome: 12 }]]), baseline: null, generated: new Set(['f_lake:5']), tombstones: new Map([[gone.id, gone]]) };
    const back = decodeDocument(encodeDocument(doc));
    expect([...back.physics!.edits]).toEqual([[3, { dh: 0.25 }], [9, { pinH: 0.5, pinBiome: 12 }]]);
    expect([...back.physics!.generated]).toEqual(['f_lake:5']);
    expect(back.physics!.tombstones.get('f_lake:4')).toEqual(gone);
    expect(back.physics!.baseline).toBeNull();   // a cache: rebuilt from the table on demand
  });
  it('round-trips the climate paint, and a file from before the brush reads as unpainted', () => {
    const doc = MapDocument.empty();
    doc.warmth[1000] = 90; doc.warmth[2000] = -127; doc.wetness[3000] = -40;
    doc.physics = { edits: new Map([[4, { dt: 0.2, dr: -0.1 }]]), baseline: null, generated: new Set(), tombstones: new Map() };
    const bytes = encodeDocument(doc);
    const back = decodeDocument(bytes);
    expect([back.warmth[1000], back.warmth[2000], back.wetness[3000], back.warmth[1]]).toEqual([90, -127, -40, 0]);
    expect(back.physics!.edits.get(4)).toEqual({ dt: 0.2, dr: -0.1 });
    // Rename the blobs in the header (same length, so every offset still holds): the file now looks pre-brush.
    const len = new DataView(bytes.buffer, bytes.byteOffset).getUint32(8, true);
    const head = new TextDecoder().decode(bytes.subarray(12, 12 + len)).replace('"warmth"', '"warmtX"').replace('"wetness"', '"wetnesX"');
    const old = bytes.slice(); old.set(new TextEncoder().encode(head), 12);
    const reread = decodeDocument(old);
    expect(reread.hasClimatePaint).toBe(false);
    expect(reread.physics!.edits.get(4)).toEqual({ dt: 0.2, dr: -0.1 });
  });
  it('round-trips land use, and lifts legacy farmland/ashlands biomes out of an older file', () => {
    const doc = MapDocument.empty();
    doc.biome.fill(3);                                   // forest everywhere
    doc.landuse.fill(1, 10, 14); doc.landuse[50] = 2;
    const back = decodeDocument(encodeDocument(doc));
    expect([...back.landuse.subarray(10, 14)]).toEqual([1, 1, 1, 1]);
    expect([back.landuse[50], back.landuse[9], back.biome[10]]).toEqual([2, 0, 3]);   // the biome underneath is intact
    // A pre-v4 file: no landuse blob, and the paint sits in the biome raster as ids 11 and 13.
    const legacy = MapDocument.empty();
    legacy.biome.fill(3); legacy.biome.fill(11, 100, 104); legacy.biome[300] = 13;
    const bytes = encodeDocument(legacy);
    const len = new DataView(bytes.buffer, bytes.byteOffset).getUint32(8, true);
    const head = new TextDecoder().decode(bytes.subarray(12, 12 + len)).replace('"landuse"', '"landusX"');
    const old = bytes.slice(); old.set(new TextEncoder().encode(head), 12);
    const reread = decodeDocument(old);
    expect([...reread.landuse.subarray(100, 104)]).toEqual([1, 1, 1, 1]);
    expect(reread.landuse[300]).toBe(2);
    expect([reread.biome[100], reread.biome[103], reread.biome[300]]).toEqual([3, 3, 3]);   // forest all round: that is what they sat on
    expect(reread.biome.includes(11) || reread.biome.includes(13)).toBe(false);
  });
  it('rejects garbage with a readable error', () => {
    expect(() => decodeDocument(new Uint8Array(64))).toThrow(/bad magic/);
  });
});

describe('vector export geometry', () => {
  it('contours a disc into one closed ring and wraps x', () => {
    const W = 64, H = 32, f = new Float32Array(W * H);
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const dx = Math.min(Math.abs(x + 0.5 - 2), W - Math.abs(x + 0.5 - 2));
      f[y * W + x] = 8 - Math.hypot(dx, y + 0.5 - 16);
    }
    const lines = contour(f, W, H, 0);
    expect(lines.length).toBe(1);
    expect(lines[0].closed).toBe(true);
    const xs = lines[0].map((p) => p[0]);
    expect(Math.max(...xs) - Math.min(...xs)).toBeLessThan(20); // continuous across the seam, not W wide
    const d = pathData(shape(lines, 0.3), 4, W, H, true, 0.5);
    expect(d.match(/M/g)!.length).toBe(2); // the ring and its wrapped copy
  });
  it('emits a river ribbon as one closed polygon', () => {
    const d = riverPath({ id: 'r', name: '', points: [100, 100, 1, 110, 100, 2, 120, 104, 3] }, 1);
    expect(d.startsWith('M')).toBe(true);
    expect(d.endsWith('Z')).toBe(true);
    expect(d.split('L').length).toBe(6);
  });
});

describe('Core biome ids in the core pack', () => {
  // The BiomeType ids the generator can emit (LocalMapInfo.cs): 0-9, plus the desert family at 12 and 14-16.
  const CORE_IDS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 14, 15, 16];
  const pack = corePack as unknown as { biomes: { id: number; name: string }[]; themes: { biomes: Record<string, string> }[]; rules: { id: string; biomes?: number[] }[] };
  it('defines a biome entry, a name and a color in every theme for every id', () => {
    for (const id of CORE_IDS) {
      expect(pack.biomes.some((b) => b.id === id), `biome ${id}`).toBe(true);
      expect(BIOME_NAMES[id], `name ${id}`).toBeTruthy();
      for (const t of pack.themes) expect(t.biomes[String(id)], `theme color ${id}`).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });
  it('dresses every desert type with stamps', () => {
    const covered = new Set(pack.rules.flatMap((r) => r.biomes ?? []));
    for (const id of [2, 12, 14, 16]) expect(covered.has(id), `rule for biome ${id}`).toBe(true);
  });
});
