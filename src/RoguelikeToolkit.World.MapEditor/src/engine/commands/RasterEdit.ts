import type { Rect } from '../core/geo';
import type { Command } from './Command';

type Raster = Float32Array | Uint8Array;

/** Block size for copy-on-write snapshots: 64x64 texels (16 KB of Float32). */
export const BLOCK = 64;

export interface RasterTarget<T extends Raster> {
  data: T;
  width: number;
  height: number;
  /** Notifies the document (GPU upload, derived caches) that a region changed. */
  touched(rect: Rect): void;
}

/**
 * One brush stroke on one raster, as an undoable command. Copy-on-write per 64x64 block: before a
 * dab writes into a block for the first time in this stroke, the block's original texels are
 * saved. Undo cost is proportional to what the stroke touched, never to the raster size.
 */
export class RasterEdit<T extends Raster> implements Command {
  private readonly before = new Map<number, T>();
  private readonly after = new Map<number, T>();
  private bounds: Rect | null = null;
  private readonly bw: number;

  constructor(readonly label: string, private readonly target: RasterTarget<T>) {
    this.bw = Math.ceil(target.width / BLOCK);
  }

  /** Call before writing into `rect` (texel coords, x may be negative/past width: wraps). */
  prepare(rect: Rect): void {
    const { width, height } = this.target;
    const y0 = Math.max(0, Math.floor(rect.y0 / BLOCK)), y1 = Math.min(Math.ceil(height / BLOCK) - 1, Math.floor(rect.y1 / BLOCK));
    const x0 = Math.floor(rect.x0 / BLOCK), x1 = Math.floor(rect.x1 / BLOCK);
    const nbx = Math.ceil(width / BLOCK);
    for (let by = y0; by <= y1; by++) {
      for (let bxRaw = x0; bxRaw <= x1; bxRaw++) {
        const bx = ((bxRaw % nbx) + nbx) % nbx;
        const key = by * this.bw + bx;
        if (!this.before.has(key)) this.before.set(key, this.copyBlock(bx, by));
      }
    }
    this.bounds = this.bounds
      ? { x0: Math.min(this.bounds.x0, rect.x0), y0: Math.min(this.bounds.y0, rect.y0), x1: Math.max(this.bounds.x1, rect.x1), y1: Math.max(this.bounds.y1, rect.y1) }
      : { ...rect };
  }

  /** Ends the stroke: captures the after-image for redo. Returns false if nothing was touched. */
  commit(): boolean {
    for (const key of this.before.keys()) this.after.set(key, this.copyBlock(key % this.bw, Math.floor(key / this.bw)));
    return this.before.size > 0;
  }

  do(): void { this.apply(this.after); }
  undo(): void { this.apply(this.before); }

  get blockCount(): number { return this.before.size; }

  private apply(blocks: Map<number, T>): void {
    for (const [key, block] of blocks) this.pasteBlock(key % this.bw, Math.floor(key / this.bw), block);
    if (this.bounds) this.target.touched(this.bounds);
  }

  private copyBlock(bx: number, by: number): T {
    const { data, width, height } = this.target;
    const w = Math.min(BLOCK, width - bx * BLOCK), h = Math.min(BLOCK, height - by * BLOCK);
    const out = new (data.constructor as { new (n: number): T })(w * h);
    for (let y = 0; y < h; y++) {
      const src = (by * BLOCK + y) * width + bx * BLOCK;
      out.set(data.subarray(src, src + w) as never, y * w);
    }
    return out;
  }

  private pasteBlock(bx: number, by: number, block: T): void {
    const { data, width, height } = this.target;
    const w = Math.min(BLOCK, width - bx * BLOCK), h = Math.min(BLOCK, height - by * BLOCK);
    for (let y = 0; y < h; y++) data.set(block.subarray(y * w, (y + 1) * w) as never, (by * BLOCK + y) * width + bx * BLOCK);
  }
}
