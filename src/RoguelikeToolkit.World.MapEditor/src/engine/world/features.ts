// Cartographic features from Core's sparse catalogs: what deserves a name, a label and an entry in
// the inspector; where it sits; which way it runs; what is true about it. Pure functions over flat
// arrays (runs in the world worker, and in tests). Core decides what exists (rivers, water bodies,
// ranges, deposits, viable settlement sites); this decides how a cartographer presents it (which
// sites become cities, how an ocean splits into named seas, where a label anchors).
import { MAP_H, dxWrap, vecToMap } from '../core/geo';
import { hash2, mulberry32 } from '../core/rng';
import {
  DEPOSIT_TYPES, NAME_KIND, NameCtx,
  type DepositType, type FeatureKind, type FeatureSet, type MapFeature, type NameStyle, type RiverGeometry,
} from './protocol';

export interface CatalogInput {
  seed: number;
  vectors: Float64Array;
  faces: Int32Array;
  heights: ArrayLike<number>;
  biomes: Uint8Array;
  flow: ArrayLike<number>;
  /** temperature, precipitation interleaved. */
  climate: ArrayLike<number>;
  tilePlate: Uint32Array;
  /** Flattened catalogs, see the engine's WorldExports. */
  rivers: Int32Array;
  waterBodies: Int32Array;
  ranges: Int32Array;
  deposits: Int32Array;
  /** (tile, score*1000) pairs, best first. */
  citySites: Int32Array;
  styles: NameStyle[];
}

// Ids 10, 11 and 13 are editor paint-only biomes (Savanna, Farmland, Ashlands); 12 and 14..16 are
// the Core desert family (Badlands, Rock desert, Salt flat, Cold desert), so ids stay stable across both.
export const BIOME_NAMES = ['Ocean', 'Plains', 'Desert', 'Forest', 'Mountain', 'Tundra', 'Jungle', 'Swamp', 'Glacier', 'Canyon', 'Savanna', 'Farmland', 'Badlands', 'Ashlands', 'Rock desert', 'Salt flat', 'Cold desert'];
const Biome = { Ocean: 0, Forest: 3, Jungle: 6, Swamp: 7 } as const;
const WaterKind = { Lake: 0, Sea: 1, Ocean: 2 } as const;
const RangeKind = { MountainRange: 0, Valley: 1, Canyon: 2, Playa: 3 } as const;
const TERMINAL = ['the sea', 'a lake', 'an inland sink'];
const EARTH_AREA_KM2 = 4 * Math.PI * 6371 * 6371;

/** Tile adjacency in CSR form, derived from icosphere faces. */
export interface Adjacency { start: Int32Array; list: Int32Array; }

export function buildAdjacency(nTiles: number, faces: Int32Array): Adjacency {
  const sets: number[][] = Array.from({ length: nTiles }, () => []);
  const link = (a: number, b: number) => { if (!sets[a].includes(b)) sets[a].push(b); };
  for (let f = 0; f < faces.length; f += 3) {
    const a = faces[f], b = faces[f + 1], c = faces[f + 2];
    link(a, b); link(b, a); link(b, c); link(c, b); link(c, a); link(a, c);
  }
  const start = new Int32Array(nTiles + 1);
  for (let i = 0; i < nTiles; i++) start[i + 1] = start[i] + sets[i].length;
  const list = new Int32Array(start[nTiles]);
  for (let i = 0; i < nTiles; i++) list.set(sets[i], start[i]);
  return { start, list };
}

/** Multi-source BFS ring distance over tiles where `inside` holds; sources are inside tiles touching outside. */
export function ringDistance(adj: Adjacency, inside: (t: number) => boolean, n: number): Int32Array {
  const dist = new Int32Array(n).fill(-1);
  const queue = new Int32Array(n);
  let head = 0, tail = 0;
  for (let t = 0; t < n; t++) {
    if (!inside(t)) continue;
    for (let k = adj.start[t]; k < adj.start[t + 1]; k++) {
      if (!inside(adj.list[k])) { dist[t] = 0; queue[tail++] = t; break; }
    }
  }
  while (head < tail) {
    const t = queue[head++];
    for (let k = adj.start[t]; k < adj.start[t + 1]; k++) {
      const u = adj.list[k];
      if (dist[u] < 0 && inside(u)) { dist[u] = dist[t] + 1; queue[tail++] = u; }
    }
  }
  return dist;
}

/** Decodes `[count, (header x h, n, items...)...]`. */
export function* records(flat: Int32Array, header: number): Generator<{ head: number[]; items: Int32Array }> {
  let p = 1;
  for (let r = 0; r < flat[0]; r++) {
    const head = Array.from(flat.subarray(p, p + header)); p += header;
    const n = flat[p++];
    yield { head, items: flat.subarray(p, p + n) };
    p += n;
  }
}

interface Shape { x: number; y: number; angle: number; extent: number; }

/** Anchor + principal axis of a tile set (map space, wrap-aware around the anchor). */
export function shapeOf(tiles: ArrayLike<number>, anchor: number, xy: Float32Array): Shape {
  const ax = xy[anchor * 2], ay = xy[anchor * 2 + 1];
  let sxx = 0, syy = 0, sxy = 0, mx = 0, my = 0;
  const n = tiles.length;
  for (let i = 0; i < n; i++) { const t = tiles[i]; mx += dxWrap(ax, xy[t * 2]); my += xy[t * 2 + 1] - ay; }
  mx /= n; my /= n;
  for (let i = 0; i < n; i++) {
    const t = tiles[i];
    const dx = dxWrap(ax, xy[t * 2]) - mx, dy = xy[t * 2 + 1] - ay - my;
    sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
  }
  sxx /= n; syy /= n; sxy /= n;
  let angle = 0.5 * Math.atan2(2 * sxy, sxx - syy);
  const l1 = (sxx + syy) / 2 + Math.sqrt(((sxx - syy) / 2) ** 2 + sxy * sxy);
  // Text reads left to right: keep the axis within ±60° of horizontal, else lay it flat.
  if (angle > Math.PI / 2) angle -= Math.PI; else if (angle < -Math.PI / 2) angle += Math.PI;
  if (Math.abs(angle) > Math.PI / 3) angle = 0;
  return { x: ax, y: ay, angle, extent: Math.sqrt(Math.max(l1, 1)) * 1.8 };
}

const fmtInt = (v: number) => Math.round(v).toLocaleString('en-US');
const pct = (v: number) => `${Math.round(v * 100)}%`;
const tempWord = (t: number) => (t < 0.2 ? 'polar' : t < 0.4 ? 'cold' : t < 0.6 ? 'temperate' : t < 0.78 ? 'warm' : 'tropical');
const rainWord = (p: number) => (p < 0.15 ? 'arid' : p < 0.35 ? 'dry' : p < 0.6 ? 'moderate' : p < 0.8 ? 'wet' : 'very wet');
const DEPOSIT_LABEL: Record<DepositType, string> = {
  iron: 'Iron', copper: 'Copper', gold: 'Gold', silver: 'Silver', tin: 'Tin', leadZinc: 'Lead & zinc', uranium: 'Uranium',
  coal: 'Coal', oil: 'Oil', gas: 'Natural gas', salt: 'Salt', gems: 'Gems', bauxite: 'Bauxite',
};
export const depositLabel = (t: DepositType): string => DEPOSIT_LABEL[t];
const sizeClass = (r: number) => (r >= 0.85 ? 'rich' : r >= 0.65 ? 'large' : r >= 0.45 ? 'medium' : 'small');

/** The culture (index into the chosen styles) that names places on a plate. Shared with the editor's nameAt. */
export const styleIndexForPlate = (seed: number, plate: number, n: number): number => (n ? hash2(seed, plate) % n : 0);

export function extractFeatures(c: CatalogInput): FeatureSet {
  const n = c.heights.length;
  const adj = buildAdjacency(n, c.faces);
  const tileKm2 = EARTH_AREA_KM2 / n;
  const xy = new Float32Array(n * 2);
  for (let t = 0; t < n; t++) {
    const p = vecToMap(c.vectors[t * 3], c.vectors[t * 3 + 1], c.vectors[t * 3 + 2]);
    xy[t * 2] = p.x; xy[t * 2 + 1] = p.y;
  }
  const isLand = (t: number) => c.heights[t] >= 0;
  const inMap = (t: number) => xy[t * 2 + 1] > 2 && xy[t * 2 + 1] < MAP_H - 2; // beyond the Mercator cut
  const waterDist = ringDistance(adj, (t) => !isLand(t), n);
  const greatCircleKm = (a: number, b: number) => {
    const d = c.vectors[a * 3] * c.vectors[b * 3] + c.vectors[a * 3 + 1] * c.vectors[b * 3 + 1] + c.vectors[a * 3 + 2] * c.vectors[b * 3 + 2];
    return Math.acos(Math.max(-1, Math.min(1, d))) * 6371;
  };

  const riverTile = new Uint8Array(n);
  for (const r of records(c.rivers, 3)) for (const t of r.items) riverTile[t] = 1;
  const valleyTile = new Uint8Array(n);
  for (const r of records(c.ranges, 2)) if (r.head[1] !== RangeKind.MountainRange) for (const t of r.items) valleyTile[t] = 1;
  const depositsAt = new Map<number, { type: DepositType; richness: number }[]>();
  for (let i = 0; i < c.deposits[0]; i++) {
    const tile = c.deposits[3 + i * 4];
    const d = { type: DEPOSIT_TYPES[c.deposits[2 + i * 4]] ?? 'iron', richness: c.deposits[4 + i * 4] / 1000 };
    let list = depositsAt.get(tile);
    if (!list) depositsAt.set(tile, (list = []));
    list.push(d);
  }

  const styleOf = (t: number) => styleIndexForPlate(c.seed, c.tilePlate[t] & 0xffff, c.styles.length);
  const near = (t: number, pred: (u: number) => boolean) => {
    if (pred(t)) return true;
    for (let k = adj.start[t]; k < adj.start[t + 1]; k++) if (pred(adj.list[k])) return true;
    return false;
  };
  const contextOf = (t: number): number => {
    let ctx = 0;
    if (near(t, (u) => !isLand(u) || riverTile[u] === 1)) ctx |= NameCtx.Water;
    if (near(t, (u) => c.biomes[u] === Biome.Forest || c.biomes[u] === Biome.Jungle)) ctx |= NameCtx.Forest;
    if (near(t, (u) => c.biomes[u] === Biome.Swamp)) ctx |= NameCtx.DarkForest;
    if (c.heights[t] > 0.45) ctx |= NameCtx.Highland;
    if (valleyTile[t]) ctx |= NameCtx.Valley;
    if (near(t, (u) => depositsAt.has(u))) ctx |= NameCtx.Deposit;
    return ctx;
  };

  /** Facts shared by every area feature. */
  const areaFacts = (tiles: ArrayLike<number>, water: boolean): [string, string][] => {
    let hMax = -Infinity, hSum = 0, tSum = 0, pSum = 0;
    const biomeCount = new Map<number, number>();
    const deps = new Map<DepositType, number>();
    for (let i = 0; i < tiles.length; i++) {
      const t = tiles[i];
      hMax = Math.max(hMax, c.heights[t]); hSum += c.heights[t];
      tSum += c.climate[t * 2]; pSum += c.climate[t * 2 + 1];
      biomeCount.set(c.biomes[t], (biomeCount.get(c.biomes[t]) ?? 0) + 1);
      for (const d of depositsAt.get(t) ?? []) deps.set(d.type, (deps.get(d.type) ?? 0) + 1);
    }
    const m = tiles.length;
    const out: [string, string][] = [['Area', `${fmtInt(m * tileKm2)} km²`]];
    if (water) out.push(['Depth (mean)', (-hSum / m).toFixed(2)]);
    else {
      out.push(['Elevation (mean / max)', `${(hSum / m).toFixed(2)} / ${hMax.toFixed(2)}`]);
      out.push(['Climate', `${tempWord(tSum / m)}, ${rainWord(pSum / m)}`]);
      const top = [...biomeCount.entries()].sort((a, b) => b[1] - a[1]).slice(0, 3);
      out.push(['Biomes', top.map(([b, k]) => `${BIOME_NAMES[b] ?? b} ${pct(k / m)}`).join(', ')]);
    }
    if (deps.size) out.push(['Deposits', [...deps.entries()].map(([d, k]) => `${DEPOSIT_LABEL[d]} ×${k}`).join(', ')]);
    return out;
  };
  const pointFacts = (t: number): [string, string][] => {
    const plate = c.tilePlate[t];
    return [
      ['Elevation', (c.heights[t] as number).toFixed(2)],
      ['Biome', BIOME_NAMES[c.biomes[t]] ?? String(c.biomes[t])],
      ['Climate', `${tempWord(c.climate[t * 2])}, ${rainWord(c.climate[t * 2 + 1])}`],
      ['Plate', `#${plate & 0xffff} (${(plate >>> 24) & 1 ? 'oceanic' : 'continental'})`],
    ];
  };

  const features: MapFeature[] = [];
  const add = (key: string, kind: FeatureKind, anchor: number, shape: Shape, weight: number, details: [string, string][], extra: Partial<MapFeature> = {}) => {
    const style = styleOf(anchor);
    const s = c.styles[style] ?? { race: 'human', culture: 'fantasy_common', label: 'Common' };
    const facts: [string, string][] = [...details];
    if (kind !== 'deposit') facts.push(['Culture', s.label]);
    features.push({
      key, kind, x: shape.x, y: shape.y, angle: shape.angle, extent: shape.extent, weight, name: '', style, details: facts,
      request: kind === 'deposit' ? undefined
        : { kind: NAME_KIND[kind], seed: hash2(c.seed, hashKey(key)) & 0x7fffffff, race: s.race, culture: s.culture, context: contextOf(anchor) },
      ...extra,
    });
  };

  // --- Rivers ---------------------------------------------------------------------------------
  let minFlow = Infinity;
  for (const r of records(c.rivers, 3)) for (const t of r.items) if (c.flow[t] > 0) minFlow = Math.min(minFlow, c.flow[t]);
  const rivers: RiverGeometry[] = [];
  for (const r of records(c.rivers, 3)) {
    const [id, terminal] = r.head;
    const path = Array.from(r.items).filter(inMap);
    if (path.length < 3) continue;
    const rng = mulberry32(hash2(c.seed, 0x5a11 + id));
    const pts: number[] = [];
    let prevX = xy[path[0] * 2], lengthKm = 0;
    for (let i = 0; i < path.length; i++) {
      const t = path[i];
      if (i > 0) lengthKm += greatCircleKm(path[i - 1], t);
      let x = xy[t * 2];
      x = prevX + dxWrap(prevX, x); prevX = x;
      let y = xy[t * 2 + 1];
      const w = Math.min(2.4, 0.35 + 0.32 * Math.log(1 + c.flow[t] / minFlow));
      // The terminal tile is open water: stop halfway, at the coast.
      if (i === path.length - 1 && !isLand(t) && pts.length >= 3) {
        x = (x + pts[pts.length - 3]) / 2; y = (y + pts[pts.length - 2]) / 2;
      }
      pts.push(x, y, w);
    }
    rivers.push({ points: meander(pts, rng) });
    const mid = path[Math.floor(path.length * 0.55)];
    const mouth = path[path.length - 1];
    add(`river:${id}`, 'river', mid, { x: xy[mid * 2], y: xy[mid * 2 + 1], angle: 0, extent: 0 }, path.length, [
      ['Length', `${fmtInt(lengthKm * 1.25)} km`], // ×1.25: tile chains cut meanders short
      ['Discharge at mouth', (c.flow[mouth] / minFlow).toFixed(0) + ' × source'],
      ['Flows into', TERMINAL[terminal] ?? 'unknown'],
      ['Source elevation', (c.heights[path[0]] as number).toFixed(2)],
    ], { river: rivers.length - 1, tiles: Int32Array.from(path) });
  }

  // --- Water bodies: lakes, seas, and an ocean divided into its basins -------------------------
  // Core already hands over connected bodies: an enclosed marine body is one sea and stays one
  // feature, however large. Only the world ocean is divided, along geography (see oceanBasins).
  const oceanTarget = Math.max(60, Math.round(n * 0.03));
  for (const b of records(c.waterBodies, 2)) {
    const [id, kind] = b.head;
    if (kind === WaterKind.Lake) {
      const tiles = Array.from(b.items).filter(inMap);
      if (tiles.length < 2) continue;
      const anchor = deepest(tiles, waterDist);
      add(`lake:${id}`, 'lake', anchor, shapeOf(tiles, anchor, xy), tiles.length, areaFacts(tiles, true), { tiles: Int32Array.from(tiles) });
      continue;
    }
    const basins = kind === WaterKind.Ocean ? oceanBasins(b.items, adj, waterDist, n) : [Array.from(b.items)];
    for (const [ri, basin] of basins.entries()) {
      const region = basin.filter(inMap);
      if (region.length < 8) continue;
      const anchor = deepest(region, waterDist);
      const big = kind === WaterKind.Ocean && region.length > oceanTarget * 0.8;
      add(`sea:${id}:${ri}`, big ? 'ocean' : 'sea', anchor, shapeOf(region, anchor, xy), region.length, areaFacts(region, true), { tiles: Int32Array.from(region) });
    }
  }

  // --- Ranges and valleys ---------------------------------------------------------------------
  for (const r of records(c.ranges, 2)) {
    const [id, kind] = r.head;
    const tiles = Array.from(r.items).filter(inMap);
    if (tiles.length < 4 || kind === RangeKind.Playa) continue;
    const anchor = central(tiles, xy);
    const facts = areaFacts(tiles, false);
    if (kind === RangeKind.Canyon) facts.unshift(['Type', 'Canyon']);
    add(`range:${id}`, kind === RangeKind.MountainRange ? 'mountain' : 'valley', anchor, shapeOf(tiles, anchor, xy), tiles.length, facts, { tiles: Int32Array.from(tiles) });
  }

  // --- Forests: connected forest/jungle regions big enough to deserve a name -------------------
  const forest = (t: number) => c.biomes[t] === Biome.Forest || c.biomes[t] === Biome.Jungle;
  const forestDist = ringDistance(adj, forest, n);
  const seen = new Uint8Array(n);
  for (let t = 0; t < n; t++) {
    if (seen[t] || !forest(t)) continue;
    const tiles = component(t, forest, adj, seen).filter(inMap);
    if (tiles.length < Math.max(12, n * 0.002)) continue;
    const anchor = deepest(tiles, forestDist);
    add(`forest:${t}`, 'forest', anchor, shapeOf(tiles, anchor, xy), tiles.length, areaFacts(tiles, false), { tiles: Int32Array.from(tiles) });
  }

  // --- Settlements: a hierarchy with spacing (Core scores sites; spacing is a map decision) ------
  // Angular spacing is planet-relative, so the same world reads the same at any icosphere size.
  const tiers: { kind: FeatureKind; count: number; spacing: number }[] = [
    { kind: 'city', count: 18, spacing: 0.2 },
    { kind: 'town', count: 42, spacing: 0.11 },
    { kind: 'village', count: 90, spacing: 0.065 },
  ];
  const picked: { tile: number; kind: FeatureKind; score: number }[] = [];
  const far = (t: number, rad: number) => {
    const cos = Math.cos(rad);
    for (const p of picked) {
      const d = c.vectors[t * 3] * c.vectors[p.tile * 3] + c.vectors[t * 3 + 1] * c.vectors[p.tile * 3 + 1] + c.vectors[t * 3 + 2] * c.vectors[p.tile * 3 + 2];
      if (d > cos) return false;
    }
    return true;
  };
  for (const tier of tiers) {
    let placed = 0;
    for (let i = 0; i < c.citySites.length && placed < tier.count; i += 2) {
      const t = c.citySites[i];
      if (!inMap(t) || !far(t, tier.spacing)) continue;
      picked.push({ tile: t, kind: tier.kind, score: c.citySites[i + 1] / 1000 }); placed++;
    }
  }
  // The best city of each landmass is its capital.
  const landSeen = new Uint8Array(n);
  const landmass = new Int32Array(n).fill(-1);
  let massId = 0;
  for (let t = 0; t < n; t++) {
    if (landSeen[t] || !isLand(t)) continue;
    for (const u of component(t, isLand, adj, landSeen)) landmass[u] = massId;
    massId++;
  }
  const hasCapital = new Set<number>();
  for (const p of picked) {
    if (p.kind !== 'city' || hasCapital.has(landmass[p.tile])) continue;
    hasCapital.add(landmass[p.tile]); p.kind = 'capital';
  }
  const tierWeight: Partial<Record<FeatureKind, number>> = { capital: 4, city: 3, town: 2, village: 1 };
  const tierName: Partial<Record<FeatureKind, string>> = { capital: 'Capital', city: 'City', town: 'Town', village: 'Village' };
  for (const p of picked) {
    const t = p.tile;
    const rng = mulberry32(hash2(c.seed, 0x6000 + t));
    // Settle off the tile center a little: tile centers sit on a visible lattice.
    const shape = { x: xy[t * 2] + (rng() - 0.5) * 4, y: xy[t * 2 + 1] + (rng() - 0.5) * 4, angle: 0, extent: 0 };
    const access = [near(t, (u) => !isLand(u)) && 'coast', riverTile[t] && 'river'].filter(Boolean).join(' & ') || 'inland';
    const res = new Set<string>();
    for (let k = adj.start[t]; k < adj.start[t + 1]; k++) for (const d of depositsAt.get(adj.list[k]) ?? []) res.add(DEPOSIT_LABEL[d.type]);
    for (const d of depositsAt.get(t) ?? []) res.add(DEPOSIT_LABEL[d.type]);
    add(`settlement:${t}`, p.kind, t, shape, tierWeight[p.kind] ?? 1, [
      ['Rank', tierName[p.kind] ?? p.kind],
      ['Site score', p.score.toFixed(2)],
      ['Water access', access],
      ...pointFacts(t),
      ...(res.size ? [['Nearby resources', [...res].join(', ')] as [string, string]] : []),
    ]);
  }

  // --- Deposits: point features, filterable by type ---------------------------------------------
  for (let i = 0; i < c.deposits[0]; i++) {
    const id = c.deposits[1 + i * 4], t = c.deposits[3 + i * 4];
    if (!inMap(t)) continue;
    const type = DEPOSIT_TYPES[c.deposits[2 + i * 4]] ?? 'iron';
    const richness = c.deposits[4 + i * 4] / 1000;
    const rng = mulberry32(hash2(c.seed, 0x7000 + id));
    add(`deposit:${id}`, 'deposit', t, { x: xy[t * 2] + (rng() - 0.5) * 3, y: xy[t * 2 + 1] + (rng() - 0.5) * 3, angle: 0, extent: 0 }, richness, [
      ['Resource', DEPOSIT_LABEL[type]],
      ['Richness', `${pct(richness)} (${sizeClass(richness)})`],
      ...pointFacts(t),
    ], { deposit: { type, richness } });
    features[features.length - 1].name = DEPOSIT_LABEL[type];
  }

  return { features, rivers };
}

function hashKey(key: string): number {
  let h = 2166136261;
  for (let i = 0; i < key.length; i++) { h ^= key.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

function component(start: number, pred: (t: number) => boolean, adj: Adjacency, seen: Uint8Array): number[] {
  const out = [start]; seen[start] = 1;
  for (let h = 0; h < out.length; h++) {
    const t = out[h];
    for (let k = adj.start[t]; k < adj.start[t + 1]; k++) {
      const u = adj.list[k];
      if (!seen[u] && pred(u)) { seen[u] = 1; out.push(u); }
    }
  }
  return out;
}

/** Tile farthest from the region's edge (ties: first). */
function deepest(tiles: number[], dist: Int32Array): number {
  let best = tiles[0];
  for (const t of tiles) if (dist[t] > dist[best]) best = t;
  return best;
}

/** Tile nearest to the region's centroid (keeps a range label on the range). */
function central(tiles: number[], xy: Float32Array): number {
  const ax = xy[tiles[0] * 2];
  let mx = 0, my = 0;
  for (const t of tiles) { mx += dxWrap(ax, xy[t * 2]); my += xy[t * 2 + 1]; }
  mx = ax + mx / tiles.length; my /= tiles.length;
  let best = tiles[0], bd = Infinity;
  for (const t of tiles) {
    const d = dxWrap(mx, xy[t * 2]) ** 2 + (xy[t * 2 + 1] - my) ** 2;
    if (d < bd) { bd = d; best = t; }
  }
  return best;
}

/** Narrowest neck, in radians of arc, that still separates one basin from another. */
const BASIN_NECK = 0.5;
/** Open-water cores smaller than this are noise, not a basin of their own. */
const MIN_CORE_TILES = 4;

/**
 * Divides the world ocean into its basins along geography instead of cutting it into equal
 * pieces. The body is eroded to its open water (tiles at least `BASIN_NECK/2` of arc from any
 * coast): basins joined only through a strait narrower than the neck fall apart into separate
 * cores, while bays and coastal fringes stay attached to their core. Every remaining water tile
 * then joins the nearest core by flood fill, so each basin is connected and its border follows
 * straits and capes. Result is largest basin first; a body with a single core is returned whole.
 * Deterministic: cores and flood order depend only on tile indices.
 */
export function oceanBasins(body: ArrayLike<number>, adj: Adjacency, depth: Int32Array, n: number, neck = BASIN_NECK): number[][] {
  const tiles = Array.from(body).sort((a, b) => a - b);
  const spacing = Math.sqrt((4 * Math.PI) / n);
  const reach = Math.max(2, Math.round(neck / 2 / spacing));
  const inBody = new Uint8Array(n);
  for (const t of tiles) inBody[t] = 1;
  const open = (t: number) => inBody[t] === 1 && (depth[t] < 0 || depth[t] >= reach);

  const seen = new Uint8Array(n);
  const cores: number[][] = [];
  for (const t of tiles) {
    if (seen[t] || !open(t)) continue;
    const core = component(t, open, adj, seen);
    if (core.length >= MIN_CORE_TILES) cores.push(core);
  }
  if (cores.length <= 1) return [tiles];
  cores.sort((a, b) => b.length - a.length || a[0] - b[0]);

  const owner = new Int32Array(n).fill(-1);
  const queue: number[] = [];
  for (const [ci, core] of cores.entries()) for (const t of core) { owner[t] = ci; queue.push(t); }
  for (let h = 0; h < queue.length; h++) {
    const t = queue[h];
    for (let k = adj.start[t]; k < adj.start[t + 1]; k++) {
      const u = adj.list[k];
      if (inBody[u] && owner[u] < 0) { owner[u] = owner[t]; queue.push(u); }
    }
  }
  const basins: number[][] = cores.map(() => []);
  for (const t of tiles) if (owner[t] >= 0) basins[owner[t]].push(t);
  return basins;
}

/**
 * Turns a tile-center chain into a drawn river: midpoints pushed sideways (meanders, alternating
 * with noise), then Chaikin smoothing. Widths interpolate along; the source tapers to a hairline.
 */
export function meander(pts: number[], rng: () => number): Float32Array {
  const out: number[] = [];
  const m = pts.length / 3;
  for (let i = 0; i < m; i++) {
    const x = pts[i * 3], y = pts[i * 3 + 1], w = pts[i * 3 + 2];
    out.push(x, y, w);
    if (i === m - 1) break;
    const nx = pts[i * 3 + 3], ny = pts[i * 3 + 4], nw = pts[i * 3 + 5];
    const dx = nx - x, dy = ny - y;
    const side = (i % 2 === 0 ? 1 : -1) * (0.12 + 0.2 * rng());
    out.push((x + nx) / 2 - dy * side, (y + ny) / 2 + dx * side, (w + nw) / 2);
  }
  let cur = out;
  for (let it = 0; it < 3; it++) {
    const next: number[] = [cur[0], cur[1], cur[2]];
    const k = cur.length / 3;
    for (let i = 0; i < k - 1; i++) {
      const a = i * 3, b = a + 3;
      next.push(0.75 * cur[a] + 0.25 * cur[b], 0.75 * cur[a + 1] + 0.25 * cur[b + 1], 0.75 * cur[a + 2] + 0.25 * cur[b + 2]);
      next.push(0.25 * cur[a] + 0.75 * cur[b], 0.25 * cur[a + 1] + 0.75 * cur[b + 1], 0.25 * cur[a + 2] + 0.75 * cur[b + 2]);
    }
    next.push(cur[cur.length - 3], cur[cur.length - 2], cur[cur.length - 1]);
    cur = next;
  }
  const total = cur.length / 3;
  for (let i = 0; i < Math.min(total, 12); i++) cur[i * 3 + 2] *= 0.3 + 0.7 * (i / 12);
  return new Float32Array(cur);
}
