import { MAP_H, MAP_W } from '../core/geo';
import type { MapDocument } from '../doc/MapDocument';
import { ownersAt, type HistoryRecord } from '../world/history/record';

/** Realms are read at 1/CELL resolution: a political picture to read, not a surface to trace. */
export const CELL = 4;
export const GRID_W = MAP_W / CELL, GRID_H = MAP_H / CELL;

/** Who held each cell of the map at one turn of history. */
export interface RealmGrid {
  history: HistoryRecord;
  turn: number;
  /** Owner polity per cell (-1 unclaimed or sea), row-major `GRID_W` wide. */
  cells: Int32Array;
}

/**
 * The realm grid at a turn, shared by everything that draws territory (tint, borders). The same object comes back
 * until the document, its history or the turn changes, so users can key their own caches on its identity.
 */
export class RealmGridCache {
  private owners: Int32Array | undefined;
  private doc: MapDocument | null = null;
  private grid: RealmGrid | null = null;

  /** The grid at `turn` (-1 = the end of history); null when the document has no history. */
  current(doc: MapDocument, turn: number): RealmGrid | null {
    const history = doc.history;
    if (!history) return null;
    const at = turn < 0 ? history.turns : turn;
    const g = this.grid;
    if (g && this.doc === doc && g.history === history && g.turn === at) return g;
    this.doc = doc;
    return (this.grid = this.build(doc, history, at));
  }

  private build(doc: MapDocument, history: HistoryRecord, turn: number): RealmGrid {
    const owners = (this.owners = ownersAt(history, turn, this.owners));
    const tile = doc.world.tile, cells = new Int32Array(GRID_W * GRID_H);
    for (let cy = 0; cy < GRID_H; cy++) {
      for (let cx = 0; cx < GRID_W; cx++) {
        const t = tile[(cy * CELL + CELL / 2) * MAP_W + cx * CELL + CELL / 2];
        cells[cy * GRID_W + cx] = t >= 0 ? owners[t] : -1;
      }
    }
    return { history, turn, cells };
  }
}
