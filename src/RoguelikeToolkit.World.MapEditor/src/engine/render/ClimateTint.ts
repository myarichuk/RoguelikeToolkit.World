import { MAP_H, MAP_W } from '../core/geo';
import type { MapDocument } from '../doc/MapDocument';
import { CLIMATE_STRIDE, climateDef, rampColor, type ClimateKind } from '../world/climate';

/** Shown at 1/CELL resolution, like the capacity tint: a picture to read, not a surface to trace. */
const CELL = 4;
const W = MAP_W / CELL, H = MAP_H / CELL;
/** The map must stay legible under it: relief and coast show through. */
const ALPHA = 178;

/**
 * One climate field as a colour ramp over the whole map (sea included: water has a temperature and, for the
 * currents, is the point). Per tile, sampled through the document's tile raster; rebuilt only when the
 * climate array, the field or the document changes.
 */
export class ClimateTint {
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;
  private readonly image: ImageData;
  private builtFor: { doc: MapDocument; climate: Float32Array | undefined; kind: ClimateKind } | null = null;

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = W; this.canvas.height = H;
    this.ctx = this.canvas.getContext('2d')!;
    this.image = this.ctx.createImageData(W, H);
  }

  /** The tinted canvas for `kind`; null when the document has no climate to show. */
  current(doc: MapDocument, kind: ClimateKind): HTMLCanvasElement | null {
    const climate = doc.physics?.climate;
    if (!climate) return null;
    const b = this.builtFor;
    if (!b || b.doc !== doc || b.climate !== climate || b.kind !== kind) {
      this.rebuild(doc, climate, kind);
      this.builtFor = { doc, climate, kind };
    }
    return this.canvas;
  }

  private rebuild(doc: MapDocument, climate: Float32Array, kind: ClimateKind): void {
    const def = climateDef(kind);
    const px = this.image.data, tile = doc.world.tile, rgb = [0, 0, 0];
    for (let cy = 0; cy < H; cy++) {
      for (let cx = 0; cx < W; cx++) {
        const t = tile[(cy * CELL + CELL / 2) * MAP_W + cx * CELL + CELL / 2];
        const o = (cy * W + cx) * 4;
        if (t < 0 || (t + 1) * CLIMATE_STRIDE > climate.length) { px[o + 3] = 0; continue; }
        rampColor(def, climate[t * CLIMATE_STRIDE + def.index], rgb);
        px[o] = rgb[0]; px[o + 1] = rgb[1]; px[o + 2] = rgb[2]; px[o + 3] = ALPHA;
      }
    }
    this.ctx.putImageData(this.image, 0, 0);
  }
}
