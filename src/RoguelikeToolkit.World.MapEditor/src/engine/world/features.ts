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

import { tileNamingStyles } from './naming';
import type { Span } from './history/timeline';
import { historyPicks, historySites, type HistoryInput } from './history/historySettlements';

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
  /** Per tile name style (`tileNamingStyles`); computed from the landmasses when absent. */
  tileStyle?: Uint8Array;
  /** Flattened catalogs, see the engine's WorldExports. */
  rivers: Int32Array;
  waterBodies: Int32Array;
  ranges: Int32Array;
  deposits: Int32Array;
  /** Per-tile count of raster pixels reading as water. When present, lakes covering no water pixel are dropped as phantoms. */
  waterPixels?: ArrayLike<number>;
  /** (tile, score*1000) pairs, best first. */
  citySites: Int32Array;
  /**
   * The map's elevation raster (water below 0), when there is one: river ends are clipped to the
   * drawn shore instead of the tile chain's guess at it.
   */
  elevation?: { data: ArrayLike<number>; width: number; height: number };
  /** One comma-joined list of scoring terms per site, same order as `citySites` pairs. */
  citySiteReasons: string[];
  /** When present and not empty, history is the source of truth for inhabited places: its settlements replace the site search. */
  history?: HistoryInput;
  styles: NameStyle[];
}

// Ids match Core's `BiomeType` (Savanna 10; Badlands 12 and the rest of the desert family 14..16; Mediterranean 19). 11, 13, 17
// and 18 are the land uses' colour slots (Farmland, Ashlands, Pasture, Built), kept so old maps load; those are land use now
// (world/landuse.ts), so Core never assigns them to a biome.
export const BIOME_NAMES = ['Ocean', 'Plains', 'Desert', 'Forest', 'Mountain', 'Tundra', 'Jungle', 'Swamp', 'Glacier', 'Canyon', 'Savanna', 'Farmland', 'Badlands', 'Ashlands', 'Rock desert', 'Salt flat', 'Cold desert', 'Pasture', 'Built', 'Mediterranean'];
const Biome = { Ocean: 0, Forest: 3, Jungle: 6, Swamp: 7 } as const;
const WaterKind = { Lake: 0, Sea: 1, Ocean: 2 } as const;
const RangeKind = { MountainRange: 0, Valley: 1, Canyon: 2, Playa: 3 } as const;
/** Core's scoring terms (SettlementScoring), as a reader would say them. */
const SITE_REASON: Record<string, string> = {
  water: 'fresh water', fertile: 'fertile land', harsh: 'harsh terrain', 'winter-ice': 'sea ice in winter',
  high: 'high ground', history: 'shaped by history', 'past-events': 'past events',
};
export const siteReasons = (raw: string | undefined): string =>
  (raw ?? '').split(',').filter(Boolean).map((r) => SITE_REASON[r] ?? r).join(', ');
const TERMINAL = ['the sea', 'a lake', 'an inland sink', 'nothing: it dries out on its way'];
/** Core's RiverTerminal for the river header: Sea 0, Lake 1, Sink 2, DriesOut 3. */
const RIVER_SINK = 2;
const RIVER_DRIES_OUT = 3;
/** A river that ends on dry land: no mouth to flare, and not a tributary of anything. */
const endsDry = (terminal: number) => terminal === RIVER_SINK || terminal === RIVER_DRIES_OUT;
/** A reach ending at a confluence earns a name only at this many in-map tiles: shorter ones are drawn but stay unnamed. */
const MIN_NAMED_CONFLUENCE_TILES = 5;
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
  coal: 'Coal', oil: 'Oil', gas: 'Natural gas', salt: 'Salt', gems: 'Gems', bauxite: 'Bauxite', gypsum: 'Gypsum',
};
export const depositLabel = (t: DepositType): string => DEPOSIT_LABEL[t];
const sizeClass = (r: number) => (r >= 0.85 ? 'rich' : r >= 0.65 ? 'large' : r >= 0.45 ? 'medium' : 'small');

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

  // A realm names its places in its own tongue (the worker resolved each polity's style); elsewhere the tile's naming style
  // (nearest realm, else the landmass) decides.
  const polityStyle = new Map<number, number>();
  if (c.history?.polityIds && c.history.polityStyle) for (let i = 0; i < c.history.polityIds.length; i++) if (c.history.polityStyle[i] >= 0) polityStyle.set(c.history.polityIds[i], c.history.polityStyle[i]);
  const styleOfPolity = (polity: number): number | undefined => polityStyle.get(polity);
  const tileStyle = c.tileStyle ?? tileNamingStyles({ seed: c.seed, n, adj, isLand, owner: [], polityStyle, styleCount: c.styles.length });
  const styleOf = (t: number) => tileStyle[t];
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
  const add = (key: string, kind: FeatureKind, anchor: number, shape: Shape, weight: number, details: [string, string][], extra: Partial<MapFeature> = {}, polity = -1) => {
    const style = styleOfPolity(polity) ?? styleOf(anchor);
    const s = c.styles[style] ?? { race: 'human', culture: 'fantasy_common', label: 'Common' };
    const facts: [string, string][] = [...details];
    if (kind !== 'deposit') facts.push(['Culture', s.label]);
    features.push({
      key, kind, x: shape.x, y: shape.y, angle: shape.angle, extent: shape.extent, weight, name: '', style, tile: anchor, details: facts,
      request: kind === 'deposit' ? undefined
        : { kind: NAME_KIND[kind], seed: hash2(c.seed, hashKey(key)) & 0x7fffffff, race: s.race, culture: s.culture, context: contextOf(anchor) },
      ...extra,
    });
  };

  // --- Rivers ---------------------------------------------------------------------------------
  // Lake tiles are ponded land (height >= 0), so `isLand` alone would read a river ending in a lake
  // as ending on dry ground: a confluence, and the river would run on to the lake's middle.
  const lakeTile = new Uint8Array(n);
  for (const b of records(c.waterBodies, 2)) if (b.head[1] === WaterKind.Lake) for (const t of b.items) lakeTile[t] = 1;
  const isWater = (t: number) => !isLand(t) || lakeTile[t] === 1;
  const raster = c.elevation;
  const wetAt = raster
    ? (x: number, y: number) => {
      const W = raster.width, xi = ((Math.floor(x) % W) + W) % W, yi = Math.floor(y);
      return yi >= 0 && yi < raster.height && raster.data[yi * W + xi] < 0;
    }
    : null;
  let minFlow = Infinity;
  for (const r of records(c.rivers, 3)) for (const t of r.items) if (c.flow[t] > 0) minFlow = Math.min(minFlow, c.flow[t]);
  const widthAt = (t: number) => Math.min(5, 0.8 + 0.7 * Math.log(1 + c.flow[t] / minFlow)); // drawn map units: headwaters ~0.8 mu (a pen line: all bank ink), great rivers ~3.5 mu
  const reaches = [...records(c.rivers, 3)].map((r) => ({
    id: r.head[0], terminal: r.head[1], items: r.items, path: Array.from(r.items).filter(inMap),
    // A reach ending on dry land without dying in a sink joins a parent reach at a confluence
    // (Core reports the parent's terminal and stops the path one tile short of the parent). The
    // true tail decides: the in-map path may be cut short by the Mercator edge.
    tributary: !endsDry(r.head[1]) && !isWater(r.items[r.items.length - 1]),
    points: null as Float32Array | null,
  }));
  const ownerOf = new Int32Array(n).fill(-1);
  reaches.forEach((r, i) => { for (const t of r.path) if (ownerOf[t] < 0) ownerOf[t] = i; });
  // Parents before tributaries (Core lists a tributary after the reach it was trimmed against), so a
  // tributary can end on its parent's drawn line instead of a tile short of it.
  const order = reaches.map((_, i) => i).sort((a, b) => Number(reaches[a].tributary) - Number(reaches[b].tributary) || a - b);
  for (const ri of order) {
    const r = reaches[ri], path = r.path;
    if (path.length < 2 || (path.length < 3 && !r.tributary)) continue;
    const rng = mulberry32(hash2(c.seed, 0x5a11 + r.id));
    const pts: number[] = [];
    let prevX = xy[path[0] * 2];
    for (let i = 0; i < path.length; i++) {
      const t = path[i];
      let x = xy[t * 2];
      x = prevX + dxWrap(prevX, x); prevX = x;
      let y = xy[t * 2 + 1];
      // The terminal tile is open water: stop halfway, at the shore (the raster clip below refines it).
      if (i === path.length - 1 && isWater(t) && pts.length >= 3) {
        x = (x + pts[pts.length - 3]) / 2; y = (y + pts[pts.length - 2]) / 2;
      }
      pts.push(x, y, widthAt(t));
    }
    let joined = false;
    if (r.tributary && path[path.length - 1] === r.items[r.items.length - 1]) {
      const join = confluence(path[path.length - 1], ri, pts[pts.length - 3], pts[pts.length - 2]);
      if (join) { pts.push(prevX + dxWrap(prevX, join.x), join.y, Math.min(pts[pts.length - 1], join.w * 0.8)); joined = true; }
    }
    if (pts.length < 9) continue;
    let line = meander(pts, rng, joined);
    if (wetAt) line = clipToLand(line, wetAt, !r.tributary);
    line = taperHead(line);
    r.points = r.tributary || endsDry(r.terminal) ? line : flareMouth(line);
  }

  /**
   * Where a tributary ending at `tail` (drawn end at tx, ty) meets its parent's drawn line: the
   * parent reach on the lowest tile next to it, at the drawn point nearest the tributary's end, so
   * it runs straight in instead of hooking round to the parent tile's center.
   */
  function confluence(tail: number, self: number, tx: number, ty: number): { x: number; y: number; w: number } | null {
    let best = -1;
    for (let k = adj.start[tail]; k < adj.start[tail + 1]; k++) {
      const u = adj.list[k], o = ownerOf[u];
      if (o < 0 || o === self || !reaches[o].points) continue;
      if (best < 0 || c.heights[u] < c.heights[best]) best = u;
    }
    if (best < 0) return null;
    const p = reaches[ownerOf[best]].points!;
    let bi = 0, bd = Infinity;
    for (let i = 0; i < p.length; i += 3) {
      const d = dxWrap(tx, p[i]) ** 2 + (p[i + 1] - ty) ** 2;
      if (d < bd) { bd = d; bi = i; }
    }
    return { x: p[bi], y: p[bi + 1], w: p[bi + 2] };
  }

  const rivers: RiverGeometry[] = [];
  for (const r of reaches) {
    if (!r.points) continue;
    rivers.push({ points: r.points });
    const path = r.path;
    // Short tributaries are headwater trickles starting and ending in the hills: drawn but not named.
    if (r.tributary && path.length < MIN_NAMED_CONFLUENCE_TILES) continue;
    let lengthKm = 0;
    for (let i = 1; i < path.length; i++) lengthKm += greatCircleKm(path[i - 1], path[i]);
    const mid = path[Math.floor(path.length * 0.55)];
    const mouth = path[path.length - 1];
    add(`river:${r.id}`, 'river', mid, { x: xy[mid * 2], y: xy[mid * 2 + 1], angle: 0, extent: 0 }, path.length, [
      ['Length', `${fmtInt(lengthKm * 1.25)} km`], // ×1.25: tile chains cut meanders short
      ['Discharge at mouth', (c.flow[mouth] / minFlow).toFixed(0) + ' × source'],
      ['Flows into', r.tributary ? 'another river' : (TERMINAL[r.terminal] ?? 'unknown')],
      ['Source elevation', (c.heights[path[0]] as number).toFixed(2)],
    ], { river: rivers.length - 1, tiles: Int32Array.from(path) });
  }

  // --- Water bodies: lakes, seas, and an ocean divided into its basins -------------------------
  // Core already hands over connected bodies: an enclosed marine body is one sea and stays one
  // feature, however large. Only the world ocean is divided, along geography (see oceanBasins).
  const oceanTarget = Math.max(60, Math.round(n * 0.03));
  // Tiles of lakes that are actually drawn: settlements never stand on them (see below).
  const lakeTiles = new Set<number>();
  for (const b of records(c.waterBodies, 2)) {
    const [id, kind] = b.head;
    if (kind === WaterKind.Lake) {
      const tiles = Array.from(b.items).filter(inMap);
      if (tiles.length < 2) continue;
      if (c.waterPixels && !tiles.some((t) => (c.waterPixels?.[t] ?? 0) > 0)) continue; // phantom: ponded water with no visible water pixel
      for (const t of tiles) lakeTiles.add(t);
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
    // Core's ranges and valleys may run over lakes and inlets: the named area is the land part.
    const tiles = Array.from(r.items).filter((t) => inMap(t) && isLand(t) && !lakeTiles.has(t));
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
  const picked: { tile: number; kind: FeatureKind; score: number; reasons: string; polity?: number; span?: Span }[] = [];
  const far = (t: number, rad: number) => {
    const cos = Math.cos(rad);
    for (const p of picked) {
      const d = c.vectors[t * 3] * c.vectors[p.tile * 3] + c.vectors[t * 3 + 1] * c.vectors[p.tile * 3 + 1] + c.vectors[t * 3 + 2] * c.vectors[p.tile * 3 + 2];
      if (d > cos) return false;
    }
    return true;
  };
  // History, when the world has one, decides who lives where: its settlements are the places, the site search is not consulted.
  const fromHistory = !!c.history && c.history.settlements.length > 0;
  const historyFacts = new Map<number, [string, string][]>();
  if (fromHistory) {
    for (const h of historyPicks(c.history!, (t) => inMap(t) && !lakeTiles.has(t))) {
      picked.push({ tile: h.tile, kind: h.kind, score: NaN, reasons: '', polity: h.polity, span: h.span });
      historyFacts.set(h.tile, h.facts);
    }
  }
  for (const tier of fromHistory ? [] : tiers) {
    let placed = 0;
    for (let i = 0; i < c.citySites.length && placed < tier.count; i += 2) {
      const t = c.citySites[i];
      if (!inMap(t) || lakeTiles.has(t) || !far(t, tier.spacing)) continue; // never inside a drawn lake
      picked.push({ tile: t, kind: tier.kind, score: c.citySites[i + 1] / 1000, reasons: siteReasons(c.citySiteReasons[i / 2]) }); placed++;
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
    if (fromHistory || p.kind !== 'city' || hasCapital.has(landmass[p.tile])) continue;
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
      ...(fromHistory ? historyFacts.get(t) ?? [] : [['Site score', p.score.toFixed(2)] as [string, string]]),
      ...(p.reasons ? [['Site factors', p.reasons] as [string, string]] : []),
      ['Water access', access],
      ...pointFacts(t),
      ...(res.size ? [['Nearby resources', [...res].join(', ')] as [string, string]] : []),
    ], { span: p.span }, p.polity ?? -1);
  }

  // --- What history left: ruins, holy sites, battlefields ------------------------------------------
  if (fromHistory) {
    const settled = new Set(picked.map((p) => p.tile));
    const weight: Partial<Record<FeatureKind, number>> = { shrine: 1.5, ruin: 1, battlefield: 0.8 };
    const title: Partial<Record<FeatureKind, string>> = { shrine: 'Holy site', ruin: 'Ruin', battlefield: 'Battlefield' };
    for (const h of historySites(c.history!, settled, (t) => inMap(t) && isLand(t) && !lakeTiles.has(t))) {
      const t = h.tile, rng = mulberry32(hash2(c.seed, 0x6800 + t));
      add(`site:${t}`, h.kind, t, { x: xy[t * 2] + (rng() - 0.5) * 4, y: xy[t * 2 + 1] + (rng() - 0.5) * 4, angle: 0, extent: 0 }, weight[h.kind] ?? 1, [
        ['Rank', title[h.kind] ?? h.kind], ...h.facts, ...pointFacts(t),
      ], { span: h.span }, h.polity);
    }
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

  return { features, rivers, roads: [] };
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
 * with noise), then Chaikin smoothing. Widths interpolate along; both ends stay where they were.
 */
export function meander(pts: number[], rng: () => number, calmEnd = false): Float32Array {
  const out: number[] = [];
  const m = pts.length / 3;
  for (let i = 0; i < m; i++) {
    const x = pts[i * 3], y = pts[i * 3 + 1], w = pts[i * 3 + 2];
    out.push(x, y, w);
    if (i === m - 1) break;
    const nx = pts[i * 3 + 3], ny = pts[i * 3 + 4], nw = pts[i * 3 + 5];
    const dx = nx - x, dy = ny - y;
    // A calm end (a tributary's run into its parent) stays straight, so it meets the parent cleanly.
    const side = calmEnd && i === m - 2 ? 0 : (i % 2 === 0 ? 1 : -1) * (0.12 + 0.2 * rng());
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
  return new Float32Array(cur);
}

/** Arc length (map units) over which a river's source widens from a hairline to its channel. */
const HEAD_TAPER_MU = 9;

/** Narrows the first `HEAD_TAPER_MU` of a drawn river (x, y, w triples) to a hairline at its source. */
export function taperHead(p: Float32Array): Float32Array {
  let s = 0;
  for (let i = 0; i < p.length; i += 3) {
    if (i) s += Math.hypot(p[i] - p[i - 3], p[i + 1] - p[i - 2]);
    if (s >= HEAD_TAPER_MU) break;
    const t = s / HEAD_TAPER_MU;
    p[i + 2] *= 0.2 + 0.8 * t * (2 - t);
  }
  return p;
}

/** Arc length (map units) over which a river widens into its mouth. */
const MOUTH_FLARE_MU = 8;

/** Widens the last `MOUTH_FLARE_MU` of a river (x, y, w triples) by up to half, as it opens into the sea or a lake. */
export function flareMouth(p: Float32Array): Float32Array {
  let s = 0;
  for (let i = p.length - 3; i >= 0; i -= 3) {
    if (i < p.length - 3) s += Math.hypot(p[i] - p[i + 3], p[i + 1] - p[i + 4]);
    if (s >= MOUTH_FLARE_MU) break;
    const t = 1 - s / MOUTH_FLARE_MU;
    p[i + 2] *= 1 + 0.5 * t * t;
  }
  return p;
}

/**
 * Trims a drawn river (x, y, w triples) to the land it crosses: a run of points in water at the
 * mouth (when `mouth`) or at the source (a lake's outflow) is cut to the first point past the shore,
 * so the ribbon meets the coastline instead of running on into the lake or sea.
 */
export function clipToLand(p: Float32Array, wet: (x: number, y: number) => boolean, mouth: boolean): Float32Array {
  const m = p.length / 3;
  let a = 0, b = m - 1;
  while (a < m - 1 && wet(p[a * 3], p[a * 3 + 1])) a++;
  if (mouth) while (b > a && wet(p[b * 3], p[b * 3 + 1])) b--;
  a = Math.max(0, a - 1); b = Math.min(m - 1, b + 1);
  return a === 0 && b === m - 1 ? p : p.slice(a * 3, (b + 1) * 3);
}
