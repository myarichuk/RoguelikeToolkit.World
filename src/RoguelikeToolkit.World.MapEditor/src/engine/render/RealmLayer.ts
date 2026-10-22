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

  /** Traces each realm's edge and writes its name. The context is in map units; `zoom` keeps lines and text a steady size on screen. */
  drawBorders(ctx: CanvasRenderingContext2D, doc: MapDocument, turn: number, zoom: number): void {
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
    for (const l of labels) {
      // A name grows with its realm but never reads smaller than NAME_MIN_PX on screen.
      const size = Math.max(l.size, NAME_MIN_PX / zoom);
      ctx.font = `600 ${size}px sans-serif`;
      ctx.lineWidth = size / 5;
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.8)';
      ctx.strokeText(l.text, l.x, l.y);
      ctx.fillStyle = 'rgba(30, 24, 20, 0.9)';
      ctx.fillText(l.text, l.x, l.y);
    }
  }
}
