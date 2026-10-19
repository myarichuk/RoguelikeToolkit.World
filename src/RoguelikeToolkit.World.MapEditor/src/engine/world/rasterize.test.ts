import { describe, expect, it } from 'vitest';
import { MAP_H, MAP_W } from '../core/geo';
import { arcLatitudeExtremes, rasterize } from './rasterize';

/** An icosphere with `level` midpoint subdivisions (level 3 = 642 tiles = the editor's smallest world). */
function icosphere(level: number): { vectors: Float64Array; faces: Int32Array } {
  const t = (1 + Math.sqrt(5)) / 2;
  const v: number[][] = [[-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1]]
    .map(([x, y, z]) => { const l = Math.hypot(x, y, z); return [x / l, y / l, z / l]; });
  let f: number[][] = [[0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11], [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
    [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9], [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1]];
  for (let l = 0; l < level; l++) {
    const mid = new Map<string, number>();
    const m = (a: number, b: number): number => {
      const k = a < b ? `${a}_${b}` : `${b}_${a}`;
      let i = mid.get(k);
      if (i === undefined) {
        const p = [(v[a][0] + v[b][0]) / 2, (v[a][1] + v[b][1]) / 2, (v[a][2] + v[b][2]) / 2];
        const len = Math.hypot(p[0], p[1], p[2]);
        i = v.push([p[0] / len, p[1] / len, p[2] / len]) - 1; mid.set(k, i);
      }
      return i;
    };
    f = f.flatMap(([a, b, c]) => { const ab = m(a, b), bc = m(b, c), ca = m(c, a); return [[a, ab, ca], [b, bc, ab], [c, ca, bc], [ab, bc, ca]]; });
  }
  return { vectors: Float64Array.from(v.flat()), faces: Int32Array.from(f.flat()) };
}

describe('rasterize', () => {
  // The editor offers detail 3..7. Coarse meshes have faces whose edges bulge hundreds of rows poleward
  // of their corners (a Mercator row near the pole is a sliver of latitude), which a fixed row pad missed.
  for (const level of [2, 3, 4]) {
    it(`covers every pixel of a level-${level} icosphere`, () => {
      const { vectors, faces } = icosphere(level);
      const n = vectors.length / 3;
      const r = rasterize({ vectors, faces, heights: new Float64Array(n).fill(0.25), biomes: new Uint8Array(n).fill(1) }, MAP_W, MAP_H);
      expect(r.elevation[0]).toBeCloseTo(0.25, 5);
      expect(r.elevation[r.elevation.length - 1]).toBeCloseTo(0.25, 5);
    });
  }

  it('finds the apex of an arc between two high-latitude points', () => {
    const lat = (d: number) => (d * Math.PI) / 180;
    const pt = (la: number, lo: number) => [Math.cos(lat(la)) * Math.cos(lat(lo)), Math.cos(lat(la)) * Math.sin(lat(lo)), Math.sin(lat(la))];
    const V = Float64Array.from([...pt(80, -60), ...pt(80, 60)]);
    const e = arcLatitudeExtremes(V, 0, 1);
    expect(e.max).toBeGreaterThan(lat(80) + 0.05);         // the arc passes poleward of both endpoints
    expect(e.min).toBeCloseTo(lat(80), 6);
    const flat = arcLatitudeExtremes(Float64Array.from([...pt(0, 0), ...pt(0, 40)]), 0, 1);
    expect(flat.max).toBeCloseTo(0, 9); expect(flat.min).toBeCloseTo(0, 9);
  });

  it('carves ponded tiles into water and counts their wet pixels', () => {
    const { vectors, faces } = icosphere(2);
    const n = vectors.length / 3;
    const land = { vectors, faces, heights: new Float64Array(n).fill(0.25), biomes: new Uint8Array(n).fill(1) };
    const dry = rasterize(land, MAP_W, MAP_H);
    expect(dry.waterPixels.every((w) => w === 0)).toBe(true);

    const lakeDepth = new Float64Array(n);
    lakeDepth[7] = 0.15;
    const wet = rasterize({ ...land, lakeDepth }, MAP_W, MAP_H);
    expect(wet.waterPixels[7]).toBeGreaterThan(0);
    // Shorelines meet the land at the 0 contour; interiors deepen with the ponding.
    let min = Infinity;
    for (const v of wet.elevation) min = Math.min(min, v);
    expect(min).toBeLessThan(0);
    expect(min).toBeGreaterThanOrEqual(-0.15);
  });

  it('keeps a lake on its own tile instead of flooding every face around it', () => {
    const { vectors, faces } = icosphere(3);
    const n = vectors.length / 3;
    const lakeDepth = new Float64Array(n);
    const t0 = 40;
    lakeDepth[t0] = 0.15;
    const r = rasterize({ vectors, faces, heights: new Float64Array(n).fill(0.25), biomes: new Uint8Array(n).fill(1), lakeDepth }, MAP_W, MAP_H);
    let own = 0, wet = 0, wetOwn = 0;
    for (let i = 0; i < r.tile.length; i++) {
      const w = r.elevation[i] < 0;
      if (r.tile[i] === t0) { own++; if (w) wetOwn++; }
      if (w) wet++;
    }
    expect(wet).toBeGreaterThan(own * 0.5);              // the lake fills most of its tile...
    expect(wet).toBeLessThan(own * 1.25);                // ...and not the ring of neighbours (the old fill drew ~3x)
    expect(wetOwn / wet).toBeGreaterThan(0.8);
  });
});
