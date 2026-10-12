import { MAP_H, MAP_W, clamp, type Vec2 } from '../core/geo';

/**
 * View transform: map units <-> device pixels. `zoom` is device pixels per map unit. x wraps, so
 * a viewport can show the same map coordinate more than once; `copies()` lists the x offsets
 * (multiples of MAP_W) at which content must be drawn to fill the screen.
 */
export class Camera {
  cx = MAP_W / 2;
  cy = MAP_H / 2;
  zoom = 0.5;
  width = 1;
  height = 1;
  dpr = 1;

  get minZoom(): number { return Math.max(this.height / MAP_H, 0.05); }

  fit(): void {
    this.zoom = Math.max(this.width / MAP_W, this.height / MAP_H);
    this.cx = MAP_W / 2; this.cy = MAP_H / 2;
    this.constrain();
  }

  /** Device px -> map units (x unwrapped). */
  toMap(px: number, py: number): Vec2 {
    return { x: this.cx + (px - this.width / 2) / this.zoom, y: this.cy + (py - this.height / 2) / this.zoom };
  }

  toScreen(x: number, y: number): Vec2 {
    return { x: (x - this.cx) * this.zoom + this.width / 2, y: (y - this.cy) * this.zoom + this.height / 2 };
  }

  /** Zoom by `factor` keeping the map point under (px, py) fixed. */
  zoomAt(px: number, py: number, factor: number): void {
    const before = this.toMap(px, py);
    this.zoom = clamp(this.zoom * factor, this.minZoom, 96);
    this.cx = before.x - (px - this.width / 2) / this.zoom;
    this.cy = before.y - (py - this.height / 2) / this.zoom;
    this.constrain();
  }

  panBy(dxPx: number, dyPx: number): void {
    this.cx -= dxPx / this.zoom; this.cy -= dyPx / this.zoom;
    this.constrain();
  }

  /** Keeps y inside the map (x wraps freely, but cx stays in [0, MAP_W) to keep floats small). */
  constrain(): void {
    this.zoom = Math.max(this.zoom, this.minZoom);
    const halfH = this.height / 2 / this.zoom;
    this.cy = halfH * 2 >= MAP_H ? MAP_H / 2 : clamp(this.cy, halfH, MAP_H - halfH);
    this.cx = ((this.cx % MAP_W) + MAP_W) % MAP_W;
  }

  /** Visible map rect (x unwrapped around cx). */
  visible(): { x0: number; y0: number; x1: number; y1: number } {
    const hw = this.width / 2 / this.zoom, hh = this.height / 2 / this.zoom;
    return { x0: this.cx - hw, y0: this.cy - hh, x1: this.cx + hw, y1: this.cy + hh };
  }

  /** x offsets (k * MAP_W) whose copy of [0, MAP_W) intersects the viewport. */
  copies(margin = 0): number[] {
    const v = this.visible();
    const out: number[] = [];
    for (let k = Math.floor((v.x0 - margin) / MAP_W); k <= Math.floor((v.x1 + margin) / MAP_W); k++) out.push(k * MAP_W);
    return out;
  }
}
