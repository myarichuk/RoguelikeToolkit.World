// Deterministic randomness. Anything that ends up in a document (auto-placed stamps, names,
// variants) is a function of seeds, never of Math.random, so a world can be regenerated exactly.

/** mulberry32: tiny, fast, good enough for placement jitter. Returns floats in [0, 1). */
export function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** FNV-1a over a string, for stable seeds derived from ids. */
export function hashString(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

/** Integer hash mixing (lowbias32). */
export function hash32(x: number): number {
  x >>>= 0;
  x ^= x >>> 16; x = Math.imul(x, 0x7feb352d);
  x ^= x >>> 15; x = Math.imul(x, 0x846ca68b);
  x ^= x >>> 16;
  return x >>> 0;
}

export const hash2 = (a: number, b: number): number => hash32(a ^ hash32(b + 0x9e3779b9));
