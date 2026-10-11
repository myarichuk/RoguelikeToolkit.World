/// <reference lib="webworker" />
// Contours the coast SDF for vector export off the main thread.
import { contour, pathData, shape } from '../engine/export/contour';

export interface ContourRequest { sdf: Float32Array; w: number; h: number; scale: number; ring: number; rings: number; tolerance: number; }
export interface ContourResult { land: string; coast: string; ripples: string[]; ms: number; rings: number; }

self.onmessage = (e: MessageEvent<ContourRequest>) => {
  const t0 = performance.now();
  const { sdf, w, h, scale, ring, rings, tolerance } = e.data;
  const k = 1 / scale;
  const coast = shape(contour(sdf, w, h, 0), tolerance);
  const ripples: string[] = [];
  for (let i = 1; i <= rings; i++) ripples.push(pathData(shape(contour(sdf, w, h, -i * ring), tolerance), 6, w, h, false, k));
  const result: ContourResult = {
    land: pathData(coast, 4, w, h, true, k), coast: pathData(coast, 4, w, h, false, k), ripples, ms: performance.now() - t0, rings: coast.length,
  };
  (self as unknown as DedicatedWorkerGlobalScope).postMessage(result);
};
