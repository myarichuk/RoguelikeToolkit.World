import type { MapDocument } from '../doc/MapDocument';
import { realmColor } from '../world/history/record';
import { RealmBorders } from './RealmBorders';
import { RealmGridCache } from './RealmGrid';
import { RealmTint } from './RealmTint';

/** Screen sizes: the border width and the smallest name worth drawing. */
const BORDER_PX = 2;
const NAME_MIN_PX = 8;

/** The history overlay: who held the land (tint), where one realm ends and the next begins (outlines), and what each is called. */
export class RealmLayer {
  private readonly grids = new RealmGridCache();
  private readonly tint = new RealmTint();
  private readonly borders = new RealmBorders();

  /** Fills the land each realm held at `turn`. The context is in map units. */
  drawTint(ctx: CanvasRenderingContext2D, doc: MapDocument, turn: number): void {
    const grid = this.grids.current(doc, turn);
    if (!grid) return;
    ctx.imageSmoothingEnabled = true;
    ctx.drawImage(this.tint.current(grid), 0, 0, doc.width, doc.height);
  }

  /**
   * Realm-name boxes that could only be placed over place labels (map units): the overlay hides the place labels under
   * them, so names never print over each other. Refreshed by every `drawBorders`.
   */
  covering: Rect[] = [];

  /**
   * Traces each realm's edge and writes its name. The context is in map units; `zoom` keeps lines and text a steady size
   * on screen. Names are placed largest realm first, each at the first of its spots that hits no earlier realm name and,
   * when possible, none of `obstacles` (the place labels on screen); a name with nowhere free is left out.
   */
  drawBorders(ctx: CanvasRenderingContext2D, doc: MapDocument, turn: number, zoom: number, obstacles: readonly Rect[] = []): void {
    const grid = this.grids.current(doc, turn);
    if (!grid) return;
    const { outlines, labels } = this.borders.current(grid);
    ctx.lineJoin = 'round';
    for (const o of outlines) {
      ctx.lineWidth = (BORDER_PX + 2) / zoom;
      ctx.strokeStyle = 'rgba(20, 16, 12, 0.35)';
      ctx.stroke(o.path);
      ctx.lineWidth = BORDER_PX / zoom;
      ctx.strokeStyle = realmColor(o.id, 0.95);
      ctx.stroke(o.path);
    }
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    const placed: Rect[] = [];
    this.covering = [];
    for (const l of [...labels].sort((a, b) => b.size - a.size || a.id - b.id)) {
      // A name grows with its realm but never reads smaller than NAME_MIN_PX on screen.
      const size = Math.max(l.size, NAME_MIN_PX / zoom);
      ctx.font = `600 ${size}px sans-serif`;
      const half = ctx.measureText(l.text).width / 2 + size * 0.2, tall = size * 0.65;
      const box = (x: number, y: number): Rect => ({ x0: x - half, y0: y - tall, x1: x + half, y1: y + tall });
      const spots: [number, number][] = [[l.x, l.y], ...l.candidates];
      const free = spots.map(([x, y]) => box(x, y)).filter((r) => !placed.some((p) => overlaps(p, r)));
      const clear = free.find((r) => !obstacles.some((o) => overlaps(o, r)));
      const chosen = clear ?? free[0];
      if (!chosen) continue;
      placed.push(chosen);
      if (!clear) this.covering.push(chosen);
      const x = (chosen.x0 + chosen.x1) / 2, y = (chosen.y0 + chosen.y1) / 2;
      ctx.lineWidth = size / 5;
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.8)';
      ctx.strokeText(l.text, x, y);
      ctx.fillStyle = 'rgba(30, 24, 20, 0.9)';
      ctx.fillText(l.text, x, y);
    }
  }
}

/** An axis-aligned box in map units. */
export interface Rect { x0: number; y0: number; x1: number; y1: number }

export const overlaps = (a: Rect, b: Rect): boolean => a.x0 < b.x1 && b.x0 < a.x1 && a.y0 < b.y1 && b.y0 < a.y1;
