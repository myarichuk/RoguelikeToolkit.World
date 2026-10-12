import { MAP_H, MAP_W } from '../core/geo';
import type { MapDocument } from '../doc/MapDocument';
import { CI, CLIMATE_STRIDE } from '../world/climate';

export type FlowKind = 'wind' | 'current';

/**
 * Arrows for the prevailing wind or the ocean surface current, on a regular grid in map space. The spacing
 * doubles or halves with the zoom, so there are always about as many arrows on screen, and snaps to powers of
 * two so they do not shuffle while zooming. Length follows speed. Currents are drawn only where there is
 * water moving, and are red or blue by whether they warm or cool the tile (a Gulf Stream or a Humboldt).
 *
 * The context is already in map units (one transform per visible map copy), so widths are divided by `zoom`.
 */
export function drawFlowArrows(ctx: CanvasRenderingContext2D, doc: MapDocument, kind: FlowKind, zoom: number, view: { x0: number; y0: number; x1: number; y1: number }): void {
  const climate = doc.physics?.climate;
  if (!climate) return;
  const step = Math.min(256, Math.max(32, 2 ** Math.round(Math.log2(112 / zoom))));
  const e = kind === 'wind' ? CI.windE : CI.currentE, n = kind === 'wind' ? CI.windN : CI.currentN;
  const tile = doc.world.tile;
  ctx.save();
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  const x0 = Math.max(0, Math.floor(view.x0 / step) * step), x1 = Math.min(MAP_W, view.x1);
  const y0 = Math.max(0, Math.floor(view.y0 / step) * step), y1 = Math.min(MAP_H, view.y1);
  // Currents sit on a grid shifted by a quarter step, so they do not lie on top of the wind arrows.
  const shift = kind === 'current' ? step / 4 : 0;
  for (let y = y0 + step / 2 + shift; y < y1; y += step) {
    for (let x = x0 + step / 2 + shift; x < x1; x += step) {
      const t = tile[Math.floor(y) * MAP_W + Math.floor(x)];
      if (t < 0 || (t + 1) * CLIMATE_STRIDE > climate.length) continue;
      const east = climate[t * CLIMATE_STRIDE + e], north = climate[t * CLIMATE_STRIDE + n];
      const m = Math.hypot(east, north);
      if (m < 0.03) continue;
      if (kind === 'current' && doc.elevationAt(x, y) >= 0) continue;
      // The map's y runs north -> south, so north is up the screen: negative y.
      const ux = east / m, uy = -north / m;
      const len = step * (kind === 'wind' ? 0.5 + 0.4 * Math.min(1, m) : 0.4 + 0.3 * Math.min(1, m));
      const ax = x - (ux * len) / 2, ay = y - (uy * len) / 2, bx = x + (ux * len) / 2, by = y + (uy * len) / 2;
      const head = Math.min(len * 0.3, step * 0.14);
      let stroke = '#ffffff';
      if (kind === 'current') {
        const heat = climate[t * CLIMATE_STRIDE + CI.oceanHeat];
        stroke = heat > 0.01 ? '#e8503a' : heat < -0.01 ? '#3a86e8' : '#e6e6e6';
      }
      const path = new Path2D();
      path.moveTo(ax, ay); path.lineTo(bx, by);
      const lx = bx - ux * head - uy * head * 0.55, ly = by - uy * head + ux * head * 0.55;
      const rx = bx - ux * head + uy * head * 0.55, ry = by - uy * head - ux * head * 0.55;
      // Two glyphs so the layers cannot be mistaken for each other: wind is a thin line with an open chevron,
      // a current is a thicker, coloured line with a solid triangular head.
      if (kind === 'wind') {
        path.moveTo(lx, ly); path.lineTo(bx, by); path.lineTo(rx, ry);
        // A dark halo under the stroke keeps the arrows readable over both the pale and the saturated ramp ends.
        ctx.strokeStyle = 'rgba(20,20,30,0.7)'; ctx.lineWidth = 2.8 / zoom; ctx.stroke(path);
        ctx.strokeStyle = stroke; ctx.lineWidth = 1.3 / zoom; ctx.stroke(path);
      } else {
        ctx.strokeStyle = 'rgba(20,20,30,0.7)'; ctx.lineWidth = 4.2 / zoom; ctx.stroke(path);
        ctx.strokeStyle = stroke; ctx.lineWidth = 2.6 / zoom; ctx.stroke(path);
        const tri = new Path2D();
        tri.moveTo(bx + ux * head * 0.4, by + uy * head * 0.4); tri.lineTo(lx, ly); tri.lineTo(rx, ry); tri.closePath();
        ctx.fillStyle = stroke; ctx.lineWidth = 1 / zoom; ctx.strokeStyle = 'rgba(20,20,30,0.7)';
        ctx.stroke(tri); ctx.fill(tri);
      }
    }
  }
  ctx.restore();
}
