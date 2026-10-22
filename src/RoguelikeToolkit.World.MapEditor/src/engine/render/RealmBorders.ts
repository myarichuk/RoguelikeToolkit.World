import { shape, contour } from '../export/contour';
import { realmName } from '../world/history/record';
import { CELL, GRID_H, GRID_W, type RealmGrid } from './RealmGrid';

/** A realm's territory edge, in map units. */
export interface RealmOutline {
  id: number;
  path: Path2D;
}

/** Where a realm's name goes and how big it is (map units). */
export interface RealmLabel {
  id: number;
  text: string;
  x: number;
  y: number;
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

interface Extent { id: number; x0: number; y0: number; x1: number; y1: number; count: number; sumX: number; sumY: number; }

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
        id: e.id, text: realmName(grid.history, e.id), x: (cx + 0.5) * CELL, y: (cy + 0.5) * CELL,
        size: Math.min(LABEL_MAX, Math.max(LABEL_MIN, Math.sqrt(e.count) * CELL * 0.12)),
      });
    }
    return { outlines, labels };
  }
}
