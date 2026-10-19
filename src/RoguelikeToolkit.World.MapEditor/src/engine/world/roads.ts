import { MAP_W, vecToMap } from '../core/geo';
import { hash2 } from '../core/rng';
import type { RoadGeometry } from './protocol';

/** Ints per road segment in the engine's `HistoryRoads()`: tile A, tile B, keeper, route A, route B, founded turn, severed turn. */
export const ROAD_STRIDE = 7;

/** Drawn width (map units) of a trunk route between capitals and of a spur between a polity's settlements. */
const TRUNK_W = 1.1, SPUR_W = 0.8;
/** How far (map units) a road wanders off a tile center: centers sit on a visible lattice. */
const WANDER_MU = 2.2;
/** Corner-cutting passes: each doubles the points and rounds the hex staircase. */
const CHAIKIN = 2;
/** A run of this many wet samples or more breaks a road in two (a ferry gap); shorter ones are bridged. */
const WET_BREAK = 4;

export interface RoadSource {
  /** Flat `HistoryRoads()` output. */
  segments: ArrayLike<number>;
  /** Unit vectors per tile (xyz). */
  vectors: Float64Array;
  /** True where a map point is open water. */
  wet: (x: number, y: number) => boolean;
  seed: number;
}

/**
 * Draws history's road segments as smooth polylines. A route is a chain of tile-to-tile segments
 * sharing (route A, route B, keeper), emitted in path order; a segment that does not continue the
 * chain (a conflicting step was dropped) starts a new piece. The tile-center chain gets a small
 * deterministic wander, is rounded by corner cutting, and is cut where it would cross open water.
 * Roads are plugin-neutral: a keeper of -1 is a trunk route between capitals, any polity is a spur
 * (or whatever a plugin emitted with `AddRoad`); a severed road is kept, flagged with its turn.
 */
export function buildRoads(src: RoadSource): RoadGeometry[] {
  const n = Math.floor(src.segments.length / ROAD_STRIDE);
  const xy = (t: number): [number, number] => {
    const q = vecToMap(src.vectors[t * 3], src.vectors[t * 3 + 1], src.vectors[t * 3 + 2]);
    return [q.x, q.y];
  };
  const out: RoadGeometry[] = [];
  let chain: number[] = [], key = '', keeper = -1, severed = -1, founded = 0, a = 0, b = 0;
  const flush = () => {
    if (chain.length >= 2) {
      for (const points of road(chain, xy, src, hash2(src.seed, hash2(a, b)), keeper < 0 ? TRUNK_W : SPUR_W)) {
        out.push({ points, kind: keeper < 0 ? 'trunk' : 'spur', keeper, severed, founded, from: a, to: b });
      }
    }
    chain = [];
  };
  for (let i = 0; i < n; i++) {
    const o = i * ROAD_STRIDE;
    const ta = src.segments[o], tb = src.segments[o + 1];
    const k = `${src.segments[o + 3]}|${src.segments[o + 4]}|${src.segments[o + 2]}`;
    if (k !== key || chain[chain.length - 1] !== ta) {
      flush();
      key = k; keeper = src.segments[o + 2]; a = src.segments[o + 3]; b = src.segments[o + 4];
      severed = src.segments[o + 6]; founded = src.segments[o + 5];
      chain = [ta];
    }
    chain.push(tb);
    // A route is severed as a whole; keep the latest cut turn if its steps disagree.
    severed = Math.max(severed, src.segments[o + 6]);
    founded = Math.min(founded, src.segments[o + 5]);
  }
  flush();
  return out;
}

function road(tiles: number[], xy: (t: number) => [number, number], src: RoadSource, seed: number, width: number): Float32Array[] {
  // Tile centers, unwrapped so the road never jumps across the seam, nudged off the lattice.
  let pts: number[] = [];
  let px = 0;
  tiles.forEach((t, i) => {
    let [x, y] = xy(t);
    if (i) { while (x - px > MAP_W / 2) x -= MAP_W; while (x - px < -MAP_W / 2) x += MAP_W; }
    px = x;
    // The ends stay on their settlements.
    if (i && i < tiles.length - 1) {
      const h = hash2(seed, t);
      x += (((h & 0xffff) / 0xffff) - 0.5) * 2 * WANDER_MU;
      y += ((((h >>> 16) & 0xffff) / 0xffff) - 0.5) * 2 * WANDER_MU;
    }
    pts.push(x, y);
  });
  for (let it = 0; it < CHAIKIN; it++) pts = chaikin(pts);

  // Cut at water: a long wet run breaks the road, a short one is bridged.
  const pieces: Float32Array[] = [];
  let cur: number[] = [], wetRun: number[] = [];
  const flushPiece = () => { if (cur.length >= 4) pieces.push(Float32Array.from(cur)); cur = []; };
  for (let i = 0; i < pts.length; i += 2) {
    const x = pts[i], y = pts[i + 1];
    const wx = ((x % MAP_W) + MAP_W) % MAP_W;
    if (src.wet(wx, y)) { wetRun.push(x, y); continue; }
    if (wetRun.length / 2 >= WET_BREAK) flushPiece();
    else if (cur.length) cur.push(...wetRun);
    wetRun = [];
    cur.push(x, y);
  }
  flushPiece();
  return pieces.map((p) => {
    const m = p.length / 2, t = new Float32Array(m * 3);
    for (let i = 0; i < m; i++) { t[i * 3] = p[i * 2]; t[i * 3 + 1] = p[i * 2 + 1]; t[i * 3 + 2] = width; }
    return t;
  });
}

/** One corner-cutting pass that keeps both ends fixed. */
export function chaikin(p: number[]): number[] {
  const n = p.length / 2;
  if (n < 3) return p.slice();
  const out = [p[0], p[1]];
  for (let i = 0; i < n - 1; i++) {
    const x0 = p[i * 2], y0 = p[i * 2 + 1], x1 = p[i * 2 + 2], y1 = p[i * 2 + 3];
    out.push(0.75 * x0 + 0.25 * x1, 0.75 * y0 + 0.25 * y1, 0.25 * x0 + 0.75 * x1, 0.25 * y0 + 0.75 * y1);
  }
  out.push(p[(n - 1) * 2], p[(n - 1) * 2 + 1]);
  return out;
}

