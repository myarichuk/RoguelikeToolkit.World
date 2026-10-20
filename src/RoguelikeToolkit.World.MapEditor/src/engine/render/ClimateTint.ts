import { MAP_H, MAP_W } from '../core/geo';
import type { MapDocument } from '../doc/MapDocument';
import { CLIMATE_STRIDE, NOT_A_COLUMN, climateDef, climateValue, rampColor, type ClimateKind } from '../world/physics/climate';

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
  private builtFor: { doc: MapDocument; climate: Float32Array | undefined; seaIce: Uint8Array | undefined; kind: ClimateKind } | null = null;

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = W; this.canvas.height = H;
    this.ctx = this.canvas.getContext('2d')!;
    this.image = this.ctx.createImageData(W, H);
  }

  /** The tinted canvas for `kind`; null when the document has no climate (or, for sea ice, no ice mask) to show. */
  current(doc: MapDocument, kind: ClimateKind): HTMLCanvasElement | null {
    const climate = doc.physics?.climate, seaIce = doc.physics?.seaIce;
    if (climateDef(kind).index === NOT_A_COLUMN ? !seaIce : !climate) return null;
    const b = this.builtFor;
    if (!b || b.doc !== doc || b.climate !== climate || b.seaIce !== seaIce || b.kind !== kind) {
      this.rebuild(doc, climate, seaIce, kind);
      this.builtFor = { doc, climate, seaIce, kind };
    }
    return this.canvas;
  }

  private rebuild(doc: MapDocument, climate: Float32Array | undefined, seaIce: Uint8Array | undefined, kind: ClimateKind): void {
    const def = climateDef(kind);
    const px = this.image.data, tile = doc.world.tile, rgb = [0, 0, 0];
    const ice = def.index === NOT_A_COLUMN;
    for (let cy = 0; cy < H; cy++) {
      for (let cx = 0; cx < W; cx++) {
        const t = tile[(cy * CELL + CELL / 2) * MAP_W + cx * CELL + CELL / 2];
        const o = (cy * W + cx) * 4;
        let v = 0, clear = true;
        if (t >= 0) {
          if (ice) { v = seaIce && t < seaIce.length ? seaIce[t] : 0; clear = v === 0; } // a mask: open water and land stay clear
          else if (climate && (t + 1) * CLIMATE_STRIDE <= climate.length) { v = climateValue(def, climate, t); clear = def.pair !== undefined && v < 0.02; } // a speed of nothing (land, dead water) stays clear
        }
        if (clear) { px[o + 3] = 0; continue; }
        rampColor(def, v, rgb);
        px[o] = rgb[0]; px[o + 1] = rgb[1]; px[o + 2] = rgb[2]; px[o + 3] = ALPHA;
      }
    }
    this.ctx.putImageData(this.image, 0, 0);
  }
}
