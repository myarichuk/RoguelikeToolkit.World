import type { Rect } from '../core/geo';
import { MAP_H, MAP_W } from '../core/geo';
import type { MapDocument, RasterLayer } from '../doc/MapDocument';

/** The paint is shown at 1/CELL resolution: it is a tint to read, not a surface to trace. */
const CELL = 4;
const W = MAP_W / CELL, H = MAP_H / CELL;

// Warm/cool are the usual red/blue; wet/dry are teal-green and ochre, so the two layers never read as one.
const COLORS = {
  warmth: { pos: [226, 84, 44], neg: [52, 118, 226] },
  wetness: { pos: [24, 168, 140], neg: [204, 150, 40] },
} as const;

/**
 * A tinted, low-resolution picture of the two climate paint rasters for the overlay. Rebuilt only for the
 * cells a stroke touched (dirty rect), so painting stays cheap on a 3-megapixel map.
 */
export class ClimatePaint {
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;
  private readonly image: ImageData;
  private dirty: Rect | null = { x0: 0, y0: 0, x1: MAP_W, y1: MAP_H };
  private doc: MapDocument | null = null;
  private off: (() => void) | null = null;

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = W; this.canvas.height = H;
    this.ctx = this.canvas.getContext('2d')!;
    this.image = this.ctx.createImageData(W, H);
  }

  attach(doc: MapDocument): void {
    this.off?.();
    this.doc = doc;
    this.dirty = { x0: 0, y0: 0, x1: MAP_W, y1: MAP_H };
    this.off = doc.events.on('raster', ({ layer, rect }) => this.invalidate(layer, rect));
  }

  private invalidate(layer: RasterLayer, rect: Rect): void {
    if (layer !== 'warmth' && layer !== 'wetness') return;
    // x may be negative or past the width (a dab across the seam); the cell loop wraps, so just widen to everything.
    const wraps = rect.x0 < 0 || rect.x1 > MAP_W;
    const r = wraps ? { x0: 0, y0: rect.y0, x1: MAP_W, y1: rect.y1 } : rect;
    this.dirty = this.dirty
      ? { x0: Math.min(this.dirty.x0, r.x0), y0: Math.min(this.dirty.y0, r.y0), x1: Math.max(this.dirty.x1, r.x1), y1: Math.max(this.dirty.y1, r.y1) }
      : { ...r };
  }

  /** The tinted canvas, up to date; null when there is no paint at all to show. */
  current(): HTMLCanvasElement | null {
    if (!this.doc) return null;
    if (this.dirty) { this.rebuild(this.doc, this.dirty); this.dirty = null; }
    return this.canvas;
  }

  private rebuild(doc: MapDocument, r: Rect): void {
    const cx0 = Math.max(0, Math.floor(r.x0 / CELL)), cx1 = Math.min(W, Math.ceil(r.x1 / CELL));
    const cy0 = Math.max(0, Math.floor(r.y0 / CELL)), cy1 = Math.min(H, Math.ceil(r.y1 / CELL));
    const px = this.image.data;
    for (let cy = cy0; cy < cy1; cy++) {
      for (let cx = cx0; cx < cx1; cx++) {
        // The strongest texel in the cell, per layer: a thin stroke must not wash out in the average.
        let t = 0, w = 0;
        for (let y = cy * CELL; y < cy * CELL + CELL; y++) {
          const row = y * MAP_W + cx * CELL;
          for (let x = 0; x < CELL; x++) {
            const a = doc.warmth[row + x], b = doc.wetness[row + x];
            if (Math.abs(a) > Math.abs(t)) t = a;
            if (Math.abs(b) > Math.abs(w)) w = b;
          }
        }
        const o = (cy * W + cx) * 4;
        // Both layers in one cell: the stronger paints it, the weaker only deepens its alpha.
        const useT = Math.abs(t) >= Math.abs(w);
        const v = useT ? t : w;
        if (v === 0) { px[o + 3] = 0; continue; }
        const c = (useT ? COLORS.warmth : COLORS.wetness)[v > 0 ? 'pos' : 'neg'];
        px[o] = c[0]; px[o + 1] = c[1]; px[o + 2] = c[2];
        px[o + 3] = Math.min(235, 40 + (Math.abs(v) / 127) * 195);
      }
    }
    this.ctx.putImageData(this.image, 0, 0, cx0, cy0, cx1 - cx0, cy1 - cy0);
  }
}
