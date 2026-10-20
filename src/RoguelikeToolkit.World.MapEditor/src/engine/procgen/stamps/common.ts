// Shared by the stamp families: line weights, generator parameters and small helpers.
import { clamp } from '../ink';

// line weights (box units)
export const OUT = 1.7; // silhouettes
export const MID = 1.0; // interior structure
export const FINE = 0.5; // hatching / detail
export const ARCH = 0.6; // architecture outlines (plain strokes)

export type Params = Record<string, number>;
export type ParamSpec = Record<string, { min: number; max: number; default: number }>;

export function resolveParams(spec: ParamSpec | undefined, given: Params | undefined): Params {
  const out: Params = {};
  if (spec) for (const k of Object.keys(spec)) {
    const s = spec[k]!;
    const v = given?.[k];
    out[k] = typeof v === 'number' && isFinite(v) ? clamp(v, s.min, s.max) : s.default;
  }
  return out;
}

export const smooth01 = (t: number): number => t * t * (3 - 2 * t);
