import { shape, contour } from '../export/contour';
import { realmName } from '../world/history/record';
import { CELL, GRID_H, GRID_W, type RealmGrid } from './RealmGrid';

/** A realm's territory edge, in map units. */
export interface RealmOutline {
  id: number;
  path: Path2D;
}

/** Where a realm's name may go and how big it is (map units). */
export interface RealmLabel {
  id: number;
  text: string;
  /** Preferred spot: the realm's own cell nearest its centroid. */
  x: number;
  y: number;
  /** Fallback spots, deepest inside the realm first, for when the preferred one collides with another name. */
  candidates: [number, number][];
  size: number;
}

export interface RealmBorderSet {
  outlines: RealmOutline[];
  labels: RealmLabel[];
}

/** Smaller than this and a realm is a speck: no outline, no name. */
const MIN_CELLS = 4;
/** Simplification tolerance for the traced edge, in cells, before it is smoothed. */
const TOLERANCE = 0.6;
const LABEL_MIN = 6, LABEL_MAX = 14;

export interface Extent { id: number; x0: number; y0: number; x1: number; y1: number; count: number; sumX: number; sumY: number; }

/** Per realm: its bounding box in cells, its size and the sum of its cell coordinates (for the centroid). */
function measure(cells: Int32Array): Map<number, Extent> {
  const out = new Map<number, Extent>();
  for (let cy = 0; cy < GRID_H; cy++) {
    for (let cx = 0; cx < GRID_W; cx++) {
      const id = cells[cy * GRID_W + cx];
      if (id < 0) continue;
      const e = out.get(id);
      if (!e) out.set(id, { id, x0: cx, y0: cy, x1: cx, y1: cy, count: 1, sumX: cx, sumY: cy });
      else { e.x0 = Math.min(e.x0, cx); e.x1 = Math.max(e.x1, cx); e.y1 = cy; e.count++; e.sumX += cx; e.sumY += cy; }
    }
  }
  return out;
}

/** The edge of one realm: marching squares over a 0/1 field of just its bounding box (plus a margin), smoothed. */
function outline(cells: Int32Array, e: Extent): Path2D {
  const w = e.x1 - e.x0 + 3, h = e.y1 - e.y0 + 3, field = new Float32Array(w * h);
  for (let cy = e.y0; cy <= e.y1; cy++) {
    for (let cx = e.x0; cx <= e.x1; cx++) if (cells[cy * GRID_W + cx] === e.id) field[(cy - e.y0 + 1) * w + (cx - e.x0 + 1)] = 1;
  }
  const path = new Path2D();
  for (const line of shape(contour(field, w, h, 0.5), TOLERANCE)) {
    line.forEach(([px, py], i) => {
      const x = (px + e.x0 - 1) * CELL, y = (py + e.y0 - 1) * CELL;
      if (i === 0) path.moveTo(x, y); else path.lineTo(x, y);
    });
    if (line.closed) path.closePath();
  }
  return path;
}

/** The realm's own cell nearest its centroid, so a crescent-shaped realm is not named in the sea. */
function labelCell(cells: Int32Array, e: Extent): [number, number] {
  const mx = e.sumX / e.count, my = e.sumY / e.count;
  let best: [number, number] = [mx, my], bestD = Infinity;
  for (let cy = e.y0; cy <= e.y1; cy++) {
    for (let cx = e.x0; cx <= e.x1; cx++) {
      if (cells[cy * GRID_W + cx] !== e.id) continue;
      const d = (cx - mx) ** 2 + (cy - my) ** 2;
      if (d < bestD) { bestD = d; best = [cx, cy]; }
    }
  }
  return best;
}

/** Most fallback spots kept per realm, and how far apart (cells) they must be to be worth trying. */
const MAX_CANDIDATES = 6, CANDIDATE_SPACING = 8;

/**
 * The realm's interior cells, deepest first: a chamfer distance to the nearest cell that is not the realm's, over its
 * bounding box. Deep cells hold a name without spilling over the border; spacing keeps the list from being one blob.
 */
export function interiorCandidates(cells: Int32Array, e: Extent): [number, number][] {
  const w = e.x1 - e.x0 + 1, h = e.y1 - e.y0 + 1, d = new Float32Array(w * h);
  const own = (x: number, y: number) => x >= 0 && y >= 0 && x < w && y < h && cells[(y + e.y0) * GRID_W + (x + e.x0)] === e.id;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (!own(x, y)) { d[y * w + x] = 0; continue; }
    const up = y > 0 ? d[(y - 1) * w + x] : 0, left = x > 0 ? d[y * w + x - 1] : 0;
    d[y * w + x] = Math.min(up, left) + 1;
  }
  for (let y = h - 1; y >= 0; y--) for (let x = w - 1; x >= 0; x--) {
    if (!own(x, y)) continue;
    const down = y < h - 1 ? d[(y + 1) * w + x] : 0, right = x < w - 1 ? d[y * w + x + 1] : 0;
    d[y * w + x] = Math.min(d[y * w + x], Math.min(down, right) + 1);
  }
  const order: number[] = [];
  for (let i = 0; i < d.length; i++) if (d[i] > 1) order.push(i);
  order.sort((a, b) => d[b] - d[a] || a - b);
  const picked: [number, number][] = [];
  for (const i of order) {
    const x = i % w, y = (i - x) / w;
    if (picked.some(([px, py]) => Math.abs(px - x) + Math.abs(py - y) < CANDIDATE_SPACING)) continue;
    picked.push([x, y]);
    if (picked.length >= MAX_CANDIDATES) break;
  }
  return picked.map(([x, y]) => [(x + e.x0 + 0.5) * CELL, (y + e.y0 + 0.5) * CELL]);
}

/**
 * Realm outlines and names for the overlay, traced from the realm grid. Rebuilt only when the grid changes (a new
 * turn, document or history), so panning and zooming just replay the cached paths.
 */
export class RealmBorders {
  private builtFor: RealmGrid | null = null;
  private set: RealmBorderSet = { outlines: [], labels: [] };

  current(grid: RealmGrid): RealmBorderSet {
    if (this.builtFor !== grid) {
      this.set = this.build(grid);
      this.builtFor = grid;
    }
    return this.set;
  }

  private build(grid: RealmGrid): RealmBorderSet {
    const outlines: RealmOutline[] = [], labels: RealmLabel[] = [];
    for (const e of measure(grid.cells).values()) {
      if (e.count < MIN_CELLS) continue;
      outlines.push({ id: e.id, path: outline(grid.cells, e) });
      const [cx, cy] = labelCell(grid.cells, e);
      labels.push({
        id: e.id, text: realmName(grid.history, e.id), x: (cx + 0.5) * CELL, y: (cy + 0.5) * CELL, candidates: interiorCandidates(grid.cells, e),
        size: Math.min(LABEL_MAX, Math.max(LABEL_MIN, Math.sqrt(e.count) * CELL * 0.12)),
      });
    }
    return { outlines, labels };
  }
}
