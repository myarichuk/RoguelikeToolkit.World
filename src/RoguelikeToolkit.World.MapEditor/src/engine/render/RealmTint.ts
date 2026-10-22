import { realmRgb } from '../world/history/record';
import { GRID_H, GRID_W, type RealmGrid } from './RealmGrid';

const ALPHA = 120;

/** Who held the land, as a translucent tint for the overlay. Rebuilt only when the realm grid changes. */
export class RealmTint {
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;
  private readonly image: ImageData;
  private builtFor: RealmGrid | null = null;

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = GRID_W; this.canvas.height = GRID_H;
    this.ctx = this.canvas.getContext('2d')!;
    this.image = this.ctx.createImageData(GRID_W, GRID_H);
  }

  current(grid: RealmGrid): HTMLCanvasElement {
    if (this.builtFor !== grid) {
      this.rebuild(grid);
      this.builtFor = grid;
    }
    return this.canvas;
  }

  private rebuild(grid: RealmGrid): void {
    const px = this.image.data, colors = new Map<number, [number, number, number]>();
    for (let i = 0; i < grid.cells.length; i++) {
      const owner = grid.cells[i], o = i * 4;
      if (owner < 0) { px[o + 3] = 0; continue; }
      let c = colors.get(owner);
      if (!c) colors.set(owner, (c = realmRgb(owner)));
      px[o] = c[0]; px[o + 1] = c[1]; px[o + 2] = c[2]; px[o + 3] = ALPHA;
    }
    this.ctx.putImageData(this.image, 0, 0);
  }
}
