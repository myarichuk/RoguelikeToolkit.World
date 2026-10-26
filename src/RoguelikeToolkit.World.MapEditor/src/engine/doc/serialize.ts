import { MAP_H, MAP_W } from '../core/geo';
import { EXPANSION_ARRAYS, type ExpansionArray, type ExpansionRecord, type HistoryRecordJson } from '../world/history/record';
import { widenClimate } from '../world/physics/climate';
import { editsFromRows, editsToRows, type EditRow } from '../world/edits/edits';
import { migrateLegacyLandUse } from '../world/physics/landuse';
import { isGeneratedFeatureId, MapDocument } from './MapDocument';
import { DEFAULT_SETTINGS, type DocSettings, type FeatureEntity, type LabelEntity, type RiverEntity, type RoadEntity, type StampEntity } from './types';

// .wmap: gzip( "WMAP" | u32 version | u32 headerLength | header JSON (utf-8) | pad to 8 | blobs ).
// The header carries settings, entities and a blob table; rasters go as raw little-endian typed
// arrays (8-byte aligned so they can be viewed in place after decompression). A 2048x1536 map is
// ~22 MB raw and compresses to a few MB; JSON for rasters would be ~10x larger and slower.

const MAGIC = 0x50414d57; // "WMAP" little-endian
// v2 adds `physics` (the hand-edit table); v1 files load as worlds with no recorded edits.
// v3 adds the climate paint rasters (`warmth`, `wetness`) and two columns on the edit rows; older files load with no paint.
// v4 adds the land-use raster (`landuse`); farmland and ashlands used to be biome ids 11 and 13 and are migrated out on load.
export const WMAP_VERSION = 4;

// The history expansion's arrays go as `history.<field>` blobs (only when a run wrote any: older and feature-off files have none).
type BlobName = 'elevation' | 'biome' | 'tile' | 'tilePlate' | 'tileStyle' | 'plateDrift' | 'tileXY' | 'ownerBase' | 'ownerChanges' | 'warmth' | 'wetness' | 'landuse' | 'capacity' | 'seaIce' | 'climate'
  | `history.${ExpansionArray}`;
type Typed = Float32Array | Uint8Array | Int8Array | Int32Array | Uint32Array | Float64Array;
const CTORS = { Float32Array, Uint8Array, Int8Array, Int32Array, Uint32Array, Float64Array } as const;

interface Header {
  format: 'wmap';
  version: number;
  width: number;
  height: number;
  settings: DocSettings;
  styles: MapDocument['world']['styles'];
  features: FeatureEntity[];
  stamps: StampEntity[];
  labels: LabelEntity[];
  rivers: RiverEntity[];
  roads?: RoadEntity[];
  history?: HistoryRecordJson;
  physics?: { edits: EditRow[]; generated: string[]; tombstones: FeatureEntity[] };
  blobs: { name: BlobName; type: keyof typeof CTORS; offset: number; length: number }[];
}

/** Uncompressed container bytes (exposed for tests; `saveDocument` gzips them). */
export function encodeDocument(doc: MapDocument): Uint8Array {
  const arrays: [BlobName, Typed][] = [
    ['elevation', doc.elevation], ['biome', doc.biome], ['tile', doc.world.tile],
    ['tilePlate', doc.world.tilePlate], ['plateDrift', doc.world.plateDrift], ['tileXY', doc.world.tileXY],
    ['warmth', doc.warmth], ['wetness', doc.wetness], ['landuse', doc.landuse],
  ];
  if (doc.history) arrays.push(['ownerBase', doc.history.ownerBase], ['ownerChanges', doc.history.changes]);
  const x = doc.history?.expansion;
  if (x) for (const k of EXPANSION_ARRAYS) arrays.push([`history.${k}`, x[k]]);
  if (doc.world.tileStyle?.length) arrays.push(['tileStyle', doc.world.tileStyle]);
  // Derived and cached: written so the capacity overlay works straight after opening, never read as an input.
  if (doc.physics?.capacity) arrays.push(['capacity', doc.physics.capacity]);
  if (doc.physics?.seaIce) arrays.push(['seaIce', doc.physics.seaIce]);
  if (doc.physics?.climate) arrays.push(['climate', doc.physics.climate]);
  const blobs: Header['blobs'] = [];
  let offset = 0;
  for (const [name, a] of arrays) {
    blobs.push({ name, type: a.constructor.name as keyof typeof CTORS, offset, length: a.length });
    offset = align8(offset + a.byteLength);
  }
  const header: Header = {
    format: 'wmap', version: WMAP_VERSION, width: doc.width, height: doc.height, settings: doc.settings, styles: doc.world.styles,
    features: [...doc.features.all()], stamps: [...doc.stamps.all()], labels: [...doc.labels.all()].map(labelOut), rivers: [...doc.rivers.all()], roads: [...doc.roads.all()],
    history: doc.history ? { turns: doc.history.turns, turnYears: doc.history.turnYears, polities: doc.history.polities, events: doc.history.events, ...(x ? { expansion: expansionJson(x) } : {}) } : undefined,
    physics: doc.physics ? { edits: editsToRows(doc.physics.edits), generated: [...doc.physics.generated], tombstones: [...doc.physics.tombstones.values()] } : undefined,
    blobs,
  };
  const json = new TextEncoder().encode(JSON.stringify(header));
  const start = align8(12 + json.length);
  const out = new Uint8Array(start + offset);
  const dv = new DataView(out.buffer);
  dv.setUint32(0, MAGIC, true); dv.setUint32(4, WMAP_VERSION, true); dv.setUint32(8, json.length, true);
  out.set(json, 12);
  for (let i = 0; i < arrays.length; i++) {
    const a = arrays[i][1];
    out.set(new Uint8Array(a.buffer, a.byteOffset, a.byteLength), start + blobs[i].offset);
  }
  return out;
}

export function decodeDocument(bytes: Uint8Array): MapDocument {
  if (bytes.length < 12) throw new Error('Not a .wmap file (too short).');
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (dv.getUint32(0, true) !== MAGIC) throw new Error('Not a .wmap file (bad magic).');
  const version = dv.getUint32(4, true);
  if (version > WMAP_VERSION) throw new Error(`.wmap version ${version} is newer than this editor (${WMAP_VERSION}).`);
  const len = dv.getUint32(8, true);
  const header = JSON.parse(new TextDecoder().decode(bytes.subarray(12, 12 + len))) as Header;
  if (header.format !== 'wmap') throw new Error('Not a .wmap file (bad header).');
  if (header.width !== MAP_W || header.height !== MAP_H) throw new Error(`Map size ${header.width}x${header.height} is not supported (expected ${MAP_W}x${MAP_H}).`);
  const start = align8(12 + len);
  const blob = <T extends Typed>(name: BlobName): T => {
    const b = header.blobs.find((x) => x.name === name);
    if (!b) throw new Error(`.wmap is missing the ${name} raster.`);
    const C = CTORS[b.type];
    const size = b.length * C.BYTES_PER_ELEMENT, at = start + b.offset;
    if (at + size > bytes.length) throw new Error(`.wmap is truncated (${name}).`);
    // Copy out: the decompressed buffer may be unaligned for the type, and the document owns its arrays.
    return new C(bytes.slice(at, at + size).buffer) as T;
  };
  // Optional: files from before the climate brush carry no paint.
  const optional = (name: BlobName): Int8Array | null => (header.blobs.some((b) => b.name === name) ? blob<Int8Array>(name) : null);
  const elevation = blob<Float32Array>('elevation'), biome = blob<Uint8Array>('biome');
  if (elevation.length !== MAP_W * MAP_H || biome.length !== MAP_W * MAP_H) throw new Error('.wmap raster size mismatch.');
  const doc = new MapDocument({ ...DEFAULT_SETTINGS, ...header.settings }, elevation, biome, {
    tile: blob('tile'), tilePlate: blob('tilePlate'), tileStyle: header.blobs.some((b) => b.name === 'tileStyle') ? blob<Uint8Array>('tileStyle') : undefined, plateDrift: blob('plateDrift'), tileXY: blob('tileXY'), styles: header.styles ?? [],
  });
  // Land use arrived with v4. Older files painted farmland and ashlands as biomes 11 and 13: lift them out.
  if (header.blobs.some((b) => b.name === 'landuse')) {
    const landuse = blob<Uint8Array>('landuse');
    if (landuse.length === doc.landuse.length) doc.landuse.set(landuse);
  }
  migrateLegacyLandUse(doc.biome, doc.landuse, doc.width);
  const warmth = optional('warmth'), wetness = optional('wetness');
  if (warmth && wetness && warmth.length === doc.warmth.length && wetness.length === doc.wetness.length) { doc.warmth.set(warmth); doc.wetness.set(wetness); }
  doc.features.reset(header.features ?? []);
  doc.stamps.reset(header.stamps ?? []);
  doc.labels.reset((header.labels ?? []).map(labelIn));
  doc.rivers.reset(header.rivers ?? []);
  doc.roads.reset(header.roads ?? []);
  if (header.history && header.blobs.some((b) => b.name === 'ownerBase')) {
    const { expansion, ...rest } = header.history;
    doc.history = { ...rest, ownerBase: blob<Int32Array>('ownerBase'), changes: header.blobs.some((b) => b.name === 'ownerChanges') ? blob<Int32Array>('ownerChanges') : new Int32Array(0) };
    // The expansion needs every one of its arrays; a file missing any loads without it rather than half of it.
    if (expansion && EXPANSION_ARRAYS.every((k) => header.blobs.some((b) => b.name === `history.${k}`))) {
      const arrays = Object.fromEntries(EXPANSION_ARRAYS.map((k) => [k, blob<Int32Array>(`history.${k}`)])) as Pick<ExpansionRecord, ExpansionArray>;
      doc.history.expansion = { ...expansion, ...arrays };
    }
  }
  // The cache is only good for the world it was computed on: a length that does not match the tile count is dropped.
  const capacity = header.blobs.some((b) => b.name === 'capacity') ? blob<Uint8Array>('capacity') : undefined;
  const goodCapacity = capacity && capacity.length === (doc.world.tileXY.length / 2) * 5 ? capacity : undefined;
  const seaIce = header.blobs.some((b) => b.name === 'seaIce') ? blob<Uint8Array>('seaIce') : undefined;
  const goodSeaIce = seaIce && seaIce.length === doc.world.tileXY.length / 2 ? seaIce : undefined;
  const climate = header.blobs.some((b) => b.name === 'climate') ? blob<Float32Array>('climate') : undefined;
  const goodClimate = climate ? widenClimate(climate, doc.world.tileXY.length / 2) : undefined;
  if (header.physics) {
    doc.physics = {
      edits: editsFromRows(header.physics.edits), baseline: null,
      generated: new Set(header.physics.generated), tombstones: new Map(header.physics.tombstones.map((f) => [f.id, f])),
      capacity: goodCapacity, seaIce: goodSeaIce, climate: goodClimate,
    };
  } else if (doc.hasWorld) {
    // Pre-physics file: nothing was ever recorded, so the engine's own features are told apart by their key shape.
    doc.physics = { edits: new Map(), baseline: null, generated: new Set([...doc.features.all()].map((f) => f.id).filter(isGeneratedFeatureId)), tombstones: new Map(), capacity: goodCapacity, seaIce: goodSeaIce, climate: goodClimate };
  }
  return doc;
}

export async function saveDocument(doc: MapDocument): Promise<Blob> {
  const raw = encodeDocument(doc);
  const stream = new Blob([raw]).stream().pipeThrough(new CompressionStream('gzip'));
  return new Response(stream).blob().then((b) => new Blob([b], { type: 'application/x-wmap' }));
}

export async function loadDocument(file: Blob): Promise<MapDocument> {
  const head = new Uint8Array(await file.slice(0, 2).arrayBuffer());
  const gz = head[0] === 0x1f && head[1] === 0x8b;
  const bytes = gz
    ? new Uint8Array(await new Response(file.stream().pipeThrough(new DecompressionStream('gzip'))).arrayBuffer())
    : new Uint8Array(await file.arrayBuffer());
  return decodeDocument(bytes);
}

const align8 = (n: number) => (n + 7) & ~7;
/** An expansion record's string tables (its arrays travel as blobs). */
const expansionJson = (x: ExpansionRecord): HistoryRecordJson['expansion'] => {
  const out: Record<string, unknown> = { ...x };
  for (const k of EXPANSION_ARRAYS) delete out[k];
  return out as HistoryRecordJson['expansion'];
};
// JSON has no NaN: "style default" spacing travels as null.
const labelOut = (l: LabelEntity) => (Number.isNaN(l.spacing) ? { ...l, spacing: null as unknown as number } : l);
const labelIn = (l: LabelEntity) => (l.spacing === null || l.spacing === undefined ? { ...l, spacing: NaN } : l);
