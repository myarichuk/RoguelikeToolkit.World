// Pure raster brush kernels (unit-tested). Coordinates in texels = map units; x wraps, y clamps.
import type { Rect } from '../core/geo';
import { hash2 } from '../core/rng';

export type HeightMode = 'raise' | 'lower' | 'smooth' | 'flatten' | 'roughen' | 'land' | 'sea';

export interface Dab { x: number; y: number; radius: number; strength: number; }

/** Rect a dab touches (x unwrapped). */
export const dabRect = (d: Dab): Rect => ({ x0: Math.floor(d.x - d.radius), y0: Math.floor(d.y - d.radius), x1: Math.ceil(d.x + d.radius) + 1, y1: Math.ceil(d.y + d.radius) + 1 });

/** Smooth falloff: 1 at the center, 0 at the rim (C1 continuous). */
export const falloff = (t: number): number => (t >= 1 ? 0 : (1 - t * t) * (1 - t * t));

function forEach(W: number, H: number, d: Dab, fn: (i: number, w: number, x: number, y: number) => void): void {
  const r = d.radius, r2 = r * r;
  const y0 = Math.max(0, Math.floor(d.y - r)), y1 = Math.min(H - 1, Math.ceil(d.y + r));
  const x0 = Math.floor(d.x - r), x1 = Math.ceil(d.x + r);
  for (let y = y0; y <= y1; y++) {
    const dy = y + 0.5 - d.y;
    for (let xx = x0; xx <= x1; xx++) {
      const dx = xx + 0.5 - d.x, q = dx * dx + dy * dy;
      if (q >= r2) continue;
      const x = ((xx % W) + W) % W;
      fn(y * W + x, falloff(Math.sqrt(q) / r), x, y);
    }
  }
}

/**
 * One height dab. `target` is used by flatten (the height sampled at stroke start). Strength is
 * height units per dab at the center for raise/lower, a blend factor for the others.
 */
export function heightDab(data: Float32Array, W: number, H: number, d: Dab, mode: HeightMode, target = 0, seed = 0): Rect {
  const s = d.strength;
  if (mode === 'smooth') {
    const src = new Map<number, number>();
    forEach(W, H, d, (i, _w, x, y) => {
      let sum = 0, n = 0;
      for (let j = -2; j <= 2; j++) for (let k = -2; k <= 2; k++) {
        const yy = y + j; if (yy < 0 || yy >= H) continue;
        sum += data[yy * W + ((x + k + W) % W)]; n++;
      }
      src.set(i, sum / n);
    });
    for (const [i, avg] of src) data[i] += (avg - data[i]) * Math.min(1, s * 4) * 0.999;
    return dabRect(d);
  }
  forEach(W, H, d, (i, w, x, y) => {
    const h = data[i];
    switch (mode) {
      case 'raise': data[i] = h + s * w; break;
      case 'lower': data[i] = h - s * w; break;
      case 'flatten': data[i] = h + (target - h) * Math.min(1, s * 6 * w); break;
      case 'roughen': data[i] = h + s * w * ((hash2(seed, y * 65536 + x) / 4294967296) - 0.5) * 2; break;
      case 'land': if (h < 0.06) data[i] = h + (0.06 - h) * Math.min(1, w * 3); break;
      case 'sea': if (h > -0.08) data[i] = h + (-0.08 - h) * Math.min(1, w * 3); break;
    }
  });
  return dabRect(d);
}

/**
 * Biome paint with a dithered, noisy rim: texels near the edge flip with probability falloff,
 * so painted regions get organic borders (the colorize pass softens them further).
 */
export function biomeDab(data: Uint8Array, W: number, H: number, d: Dab, biome: number, seed: number): Rect {
  forEach(W, H, d, (i, w, x, y) => {
    const n = hash2(seed, y * 65536 + x) / 4294967296;
    if (n < Math.min(1, w * 2.2) * d.strength + (w > 0.6 ? 1 : 0)) data[i] = biome;
  });
  return dabRect(d);
}

export type ClimateMode = 'raise' | 'lower' | 'erase';

/**
 * One climate-paint dab on a signed byte raster (-127..127). Strength is the share of full scale added at
 * the center per dab, so a stroke builds up like raising terrain; erase pulls the paint back to zero.
 */
export function climateDab(data: Int8Array, W: number, H: number, d: Dab, mode: ClimateMode): Rect {
  const s = d.strength;
  forEach(W, H, d, (i, w) => {
    const v = data[i];
    if (mode === 'erase') data[i] = Math.round(v * (1 - Math.min(1, s * 6 * w)));
    else {
      const next = v + (mode === 'raise' ? 1 : -1) * s * w * 127;
      // Always move at least one step where the brush reaches, so a light touch is never rounded away.
      const step = Math.round(next) === v && w > 0.05 ? v + (mode === 'raise' ? 1 : -1) : Math.round(next);
      data[i] = Math.max(-127, Math.min(127, step));
    }
  });
  return dabRect(d);
}

/** Points along a stroke segment spaced by `spacing` (dab emission independent of pointer rate). */
export function strokePoints(ax: number, ay: number, bx: number, by: number, spacing: number, carry: number): { pts: [number, number][]; carry: number } {
  const len = Math.hypot(bx - ax, by - ay);
  const pts: [number, number][] = [];
  let t = spacing - carry;
  while (t <= len) { const f = len ? t / len : 0; pts.push([ax + (bx - ax) * f, ay + (by - ay) * f]); t += spacing; }
  return { pts, carry: len - (t - spacing) };
}
