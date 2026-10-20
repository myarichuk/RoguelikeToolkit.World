import { MAP_H, MAP_W } from '../core/geo';
import type { MapDocument } from '../doc/MapDocument';
import { CAPACITY_KINDS, CAPACITY_STRIDE, CAPACITY_INDEX, type CapacityKind } from '../world/physics/capacity';

/** Shown at 1/CELL resolution: a tint to read, not a surface to trace. */
const CELL = 4;
const W = MAP_W / CELL, H = MAP_H / CELL;

/**
 * A single-hue picture of one capacity (opacity = how much the tile can carry) for the overlay. The engine
 * produces capacity per tile, and the document maps pixels to tiles, so a cell samples the tile under its
 * centre. Rebuilt only when the capacity array, the kind or the document changes.
 */
export class CapacityTint {
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;
  private readonly image: ImageData;
  private builtFor: { doc: MapDocument; capacity: Uint8Array | undefined; kind: CapacityKind } | null = null;

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = W; this.canvas.height = H;
    this.ctx = this.canvas.getContext('2d')!;
    this.image = this.ctx.createImageData(W, H);
  }

  /** The tinted canvas for `kind`; null when the document has no capacity to show. */
  current(doc: MapDocument, kind: CapacityKind): HTMLCanvasElement | null {
    const capacity = doc.physics?.capacity;
    if (!capacity) return null;
    const b = this.builtFor;
    if (!b || b.doc !== doc || b.capacity !== capacity || b.kind !== kind) {
      this.rebuild(doc, capacity, kind);
      this.builtFor = { doc, capacity, kind };
    }
    return this.canvas;
  }

  private rebuild(doc: MapDocument, capacity: Uint8Array, kind: CapacityKind): void {
    const rgb = CAPACITY_KINDS.find((k) => k.key === kind)!.rgb, idx = CAPACITY_INDEX[kind];
    const px = this.image.data, tile = doc.world.tile;
    for (let cy = 0; cy < H; cy++) {
      for (let cx = 0; cx < W; cx++) {
        const t = tile[(cy * CELL + CELL / 2) * MAP_W + cx * CELL + CELL / 2];
        const v = t >= 0 ? capacity[t * CAPACITY_STRIDE + idx] ?? 0 : 0;
        const o = (cy * W + cx) * 4;
        px[o] = rgb[0]; px[o + 1] = rgb[1]; px[o + 2] = rgb[2];
        px[o + 3] = v === 0 ? 0 : Math.round(30 + (v / 255) * 200);
      }
    }
    this.ctx.putImageData(this.image, 0, 0);
  }
}
