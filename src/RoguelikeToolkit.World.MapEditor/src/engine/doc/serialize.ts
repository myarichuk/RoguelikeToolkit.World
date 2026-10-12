import { MAP_H, MAP_W } from '../core/geo';
import { editsFromRows, editsToRows, type EditRow } from '../world/edits';
import { migrateLegacyLandUse } from '../world/landuse';
import { isGeneratedFeatureId, MapDocument } from './MapDocument';
import { DEFAULT_SETTINGS, type DocSettings, type FeatureEntity, type LabelEntity, type RiverEntity, type StampEntity } from './types';

// .wmap: gzip( "WMAP" | u32 version | u32 headerLength | header JSON (utf-8) | pad to 8 | blobs ).
// The header carries settings, entities and a blob table; rasters go as raw little-endian typed
// arrays (8-byte aligned so they can be viewed in place after decompression). A 2048x1536 map is
// ~22 MB raw and compresses to a few MB; JSON for rasters would be ~10x larger and slower.

const MAGIC = 0x50414d57; // "WMAP" little-endian
// v2 adds `physics` (the hand-edit table); v1 files load as worlds with no recorded edits.
// v3 adds the climate paint rasters (`warmth`, `wetness`) and two columns on the edit rows; older files load with no paint.
// v4 adds the land-use raster (`landuse`); farmland and ashlands used to be biome ids 11 and 13 and are migrated out on load.
export const WMAP_VERSION = 4;

type BlobName = 'elevation' | 'biome' | 'tile' | 'tilePlate' | 'plateDrift' | 'tileXY' | 'warmth' | 'wetness' | 'landuse';
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
  const blobs: Header['blobs'] = [];
  let offset = 0;
  for (const [name, a] of arrays) {
    blobs.push({ name, type: a.constructor.name as keyof typeof CTORS, offset, length: a.length });
    offset = align8(offset + a.byteLength);
  }
  const header: Header = {
    format: 'wmap', version: WMAP_VERSION, width: doc.width, height: doc.height, settings: doc.settings, styles: doc.world.styles,
    features: [...doc.features.all()], stamps: [...doc.stamps.all()], labels: [...doc.labels.all()].map(labelOut), rivers: [...doc.rivers.all()],
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
    tile: blob('tile'), tilePlate: blob('tilePlate'), plateDrift: blob('plateDrift'), tileXY: blob('tileXY'), styles: header.styles ?? [],
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
  if (header.physics) {
    doc.physics = {
      edits: editsFromRows(header.physics.edits), baseline: null,
      generated: new Set(header.physics.generated), tombstones: new Map(header.physics.tombstones.map((f) => [f.id, f])),
    };
  } else if (doc.hasWorld) {
    // Pre-physics file: nothing was ever recorded, so the engine's own features are told apart by their key shape.
    doc.physics = { edits: new Map(), baseline: null, generated: new Set([...doc.features.all()].map((f) => f.id).filter(isGeneratedFeatureId)), tombstones: new Map() };
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
// JSON has no NaN: "style default" spacing travels as null.
const labelOut = (l: LabelEntity) => (Number.isNaN(l.spacing) ? { ...l, spacing: null as unknown as number } : l);
const labelIn = (l: LabelEntity) => (l.spacing === null || l.spacing === undefined ? { ...l, spacing: NaN } : l);
