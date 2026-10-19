// Forward rasterization of the icosphere into map-space rasters (Mercator, see core/geo.ts).
//
// For a pixel direction d, the weights (d·(b×c), d·(c×a), d·(a×b)) are all non-negative iff the
// ray hits spherical triangle abc, and, normalized, are the barycentric coordinates of that hit.
// Heights interpolate linearly across each face instead of snapping to the nearest tile, so the
// coast has no hex staircase. Categorical fields (biome, plate) take the dominant vertex; biome
// borders get a smooth per-tile jitter so they read as organic regions, not hexagons.
import { latOfY } from '../core/geo';
import { hash2 } from '../core/rng';

export interface TileFields {
  /** xyz interleaved unit vectors. */
  vectors: Float64Array;
  /** Three tile indices per face. */
  faces: Int32Array;
  heights: Float64Array;
  biomes: Uint8Array;
  /** Ponded water depth per tile (0 where dry): carved into the elevation so lakes read as water, out to about halfway to the dry neighbours. */
  lakeDepth?: ArrayLike<number>;
}

export interface Rasters {
  width: number;
  height: number;
  /** Interpolated height, sea level 0. */
  elevation: Float32Array;
  /** Biome id per pixel (organic borders). */
  biome: Uint8Array;
  /** Dominant tile per pixel (inspection, categorical lookups such as plates). */
  tile: Int32Array;
  /** Per-tile count of pixels reading as water (elevation below 0). */
  waterPixels: Int32Array;
}

/** Smooth 2D value noise in [0,1), one independent field per `salt`. */
export function valueNoise(x: number, y: number, salt: number): number {
  const xi = Math.floor(x), yi = Math.floor(y);
  let fx = x - xi, fy = y - yi;
  fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
  const h = (i: number, j: number) => hash2(salt, (i * 73856093) ^ (j * 19349663)) / 4294967296;
  const a = h(xi, yi), b = h(xi + 1, yi), c = h(xi, yi + 1), d = h(xi + 1, yi + 1);
  return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
}

/**
 * Latitude extremes of the great-circle arc between tiles p and q. The arc's highest (lowest) point is the
 * apex of its great circle when that apex lies between the endpoints; otherwise the endpoints are the extremes.
 */
export function arcLatitudeExtremes(V: ArrayLike<number>, p: number, q: number): { min: number; max: number } {
  const ax = V[p * 3], ay = V[p * 3 + 1], az = V[p * 3 + 2];
  const bx = V[q * 3], by = V[q * 3 + 1], bz = V[q * 3 + 2];
  let min = Math.min(Math.asin(clampUnit(az)), Math.asin(clampUnit(bz)));
  let max = Math.max(Math.asin(clampUnit(az)), Math.asin(clampUnit(bz)));
  // Plane normal n = a x b.
  const nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
  const nl = Math.hypot(nx, ny, nz);
  if (nl < 1e-12) return { min, max };
  // Apex of the northern half of the circle: the component of +z along the plane, normalised.
  const k = nz / (nl * nl);
  const px = -k * nx, py = -k * ny, pz = 1 - k * nz;
  const pl = Math.hypot(px, py, pz);
  if (pl < 1e-12) return { min, max };   // the equator: no bulge
  const apexLat = Math.asin(clampUnit(pz / pl));
  // On the arc iff it lies on the same side of both endpoints as the arc's interior.
  const inArc = (x: number, y: number, z: number): boolean => {
    const c1 = (ay * z - az * y) * nx + (az * x - ax * z) * ny + (ax * y - ay * x) * nz;           // (a x v) . n
    const c2 = (y * bz - z * by) * nx + (z * bx - x * bz) * ny + (x * by - y * bx) * nz;           // (v x b) . n
    return c1 >= 0 && c2 >= 0;
  };
  if (inArc(px / pl, py / pl, pz / pl)) max = Math.max(max, apexLat);
  if (inArc(-px / pl, -py / pl, -pz / pl)) min = Math.min(min, -apexLat);
  return { min, max };
}

/** Box blur radius (pixels, two passes) over the ponded share before it is thresholded: rounds the triangle-edge shore. */
const LAKE_BLUR = 3;

/**
 * Carves lakes into the elevation. A pixel is lake water where the share of its barycentric weight
 * on ponded tiles, blurred across face edges and thresholded with noise, passes about half: the
 * shore runs halfway between a ponded tile and its dry neighbours and wanders in bays instead of
 * following triangle edges. (Flooding every face that touches a ponded tile drew each lake as the
 * whole star of triangles around it: twice its size, hexagonal, and fused with any lake two tiles
 * away.) Only rows y0..y1 (plus the blur reach) hold lakes.
 */
function carveLakes(elevation: Float32Array, share: Float32Array, depth: Float32Array, W: number, H: number, y0: number, y1: number): void {
  const r0 = Math.max(0, y0 - 2 * LAKE_BLUR), r1 = Math.min(H - 1, y1 + 2 * LAKE_BLUR), h = r1 - r0 + 1;
  const a = share.subarray(r0 * W, (r1 + 1) * W), d = depth.subarray(r0 * W, (r1 + 1) * W);
  const tmp = new Float32Array(h * W);
  for (const f of [a, d]) for (let pass = 0; pass < 2; pass++) { boxBlur(f, tmp, W, h, LAKE_BLUR, true); boxBlur(tmp, f, W, h, LAKE_BLUR, false); }
  const jl = 1 / 12; // bays ~12 mu, with a ~4 mu octave on top
  for (let y = r0; y <= r1; y++) {
    for (let x = 0; x < W; x++) {
      const k = (y - r0) * W + x;
      if (a[k] <= LAKE_SHORE - LAKE_JITTER / 2) continue;
      const n = 0.6 * valueNoise(x * jl, y * jl, 0x1a4e) + 0.4 * valueNoise(x * jl * 3, y * jl * 3, 0x1a4f);
      if (a[k] > LAKE_SHORE + LAKE_JITTER * (n - 0.5)) elevation[y * W + x] = -Math.max(d[k], MIN_LAKE_DEPTH);
    }
  }
}

/** One box-blur pass along x (wrapping) or y (clamped) over a W x H field. */
export function boxBlur(src: Float32Array, dst: Float32Array, W: number, H: number, r: number, alongX: boolean): void {
  const inv = 1 / (2 * r + 1);
  if (alongX) {
    for (let y = 0; y < H; y++) {
      const row = y * W;
      let s = 0;
      for (let k = -r; k <= r; k++) s += src[row + ((k % W) + W) % W];
      for (let x = 0; x < W; x++) {
        dst[row + x] = s * inv;
        s += src[row + (x + r + 1) % W] - src[row + ((x - r) % W + W) % W];
      }
    }
  } else {
    for (let x = 0; x < W; x++) {
      let s = 0;
      for (let k = -r; k <= r; k++) s += src[Math.min(H - 1, Math.max(0, k)) * W + x];
      for (let y = 0; y < H; y++) {
        dst[y * W + x] = s * inv;
        s += src[Math.min(H - 1, y + r + 1) * W + x] - src[Math.max(0, y - r) * W + x];
      }
    }
  }
}

/** Share of a pixel's barycentric weight on ponded tiles above which it is lake water. */
const LAKE_SHORE = 0.5;
/** Peak-to-peak noise on that threshold, so shores wander instead of following triangle edges. */
const LAKE_JITTER = 0.4;
/** Depth given to lake pixels right at the shore, where the interpolated depth tends to 0. */
const MIN_LAKE_DEPTH = 0.004;

const clampUnit = (v: number): number => Math.max(-1, Math.min(1, v));

export function rasterize(t: TileFields, W: number, H: number, biomeJitter = 0.22): Rasters {
  const V = t.vectors, F = t.faces;
  const lakeDepth = t.lakeDepth;
  const elevation = new Float32Array(W * H).fill(NaN);
  const biome = new Uint8Array(W * H);
  const tile = new Int32Array(W * H);
  const nT = V.length / 3;
  const lon = new Float64Array(nT), lat = new Float64Array(nT);
  for (let i = 0; i < nT; i++) {
    lon[i] = Math.atan2(V[i * 3 + 1], V[i * 3]);
    lat[i] = Math.asin(Math.max(-1, Math.min(1, V[i * 3 + 2])));
  }
  const pxLon = (2 * Math.PI) / W;
  const rowSin = new Float64Array(H), rowCos = new Float64Array(H), rowLat = new Float64Array(H);
  for (let y = 0; y < H; y++) {
    const la = latOfY(y + 0.5);
    rowLat[y] = la; rowSin[y] = Math.sin(la); rowCos[y] = Math.cos(la);
  }
  const colSin = new Float64Array(W), colCos = new Float64Array(W);
  for (let x = 0; x < W; x++) {
    const lo = -Math.PI + (x + 0.5) * pxLon;
    colSin[x] = Math.sin(lo); colCos[x] = Math.cos(lo);
  }
  // Rows run north -> south (latitude descending): first row at or south of `la`.
  const rowAtLat = (la: number): number => {
    let lo = 0, hi = H - 1;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (rowLat[mid] > la) lo = mid + 1; else hi = mid;
    }
    return lo;
  };
  const jf = 1 / 7; // jitter feature size ~7 mu
  // Ponded share and depth per pixel, carved into the elevation after the faces (see carveLakes).
  let anyPond = false;
  if (lakeDepth) for (let k = 0; k < nT && !anyPond; k++) anyPond = lakeDepth[k] > 0;
  const lakeShare = anyPond ? new Float32Array(W * H) : null, lakeDepthPx = anyPond ? new Float32Array(W * H) : null;
  let lakeY0 = H, lakeY1 = -1;

  for (let f = 0; f < F.length; f += 3) {
    const ia = F[f], ib = F[f + 1], ic = F[f + 2];
    const ax = V[ia * 3], ay = V[ia * 3 + 1], az = V[ia * 3 + 2];
    const bx = V[ib * 3], by = V[ib * 3 + 1], bz = V[ib * 3 + 2];
    const cx = V[ic * 3], cy = V[ic * 3 + 1], cz = V[ic * 3 + 2];
    const nax = by * cz - bz * cy, nay = bz * cx - bx * cz, naz = bx * cy - by * cx;
    const nbx = cy * az - cz * ay, nby = cz * ax - cx * az, nbz = cx * ay - cy * ax;
    const ncx = ay * bz - az * by, ncy = az * bx - ax * bz, ncz = ax * by - ay * bx;
    const sgn = Math.sign(ax * nax + ay * nay + az * naz) || 1;

    let lo0 = lon[ia], lo1 = lon[ib], lo2 = lon[ic];
    let la0 = Math.min(lat[ia], lat[ib], lat[ic]), la1 = Math.max(lat[ia], lat[ib], lat[ic]);
    let lonMin: number, lonMax: number;
    const containsPole = (pz: number) =>
      sgn * pz * naz >= 0 && sgn * pz * nbz >= 0 && sgn * pz * ncz >= 0;
    if (containsPole(1)) { la1 = Math.PI / 2; lonMin = -Math.PI; lonMax = Math.PI; }
    else if (containsPole(-1)) { la0 = -Math.PI / 2; lonMin = -Math.PI; lonMax = Math.PI; }
    else {
      if (Math.max(lo0, lo1, lo2) - Math.min(lo0, lo1, lo2) > Math.PI) {
        // Straddles the antimeridian: unwrap negatives, rasterize past +pi, wrap x.
        if (lo0 < 0) lo0 += 2 * Math.PI;
        if (lo1 < 0) lo1 += 2 * Math.PI;
        if (lo2 < 0) lo2 += 2 * Math.PI;
      }
      lonMin = Math.min(lo0, lo1, lo2); lonMax = Math.max(lo0, lo1, lo2);
    }
    // Great-circle edges bulge poleward of their endpoints' latitude range: widen to each arc's true extreme
    // (a fixed row pad is wrong on coarse meshes near the poles, where a row is a tiny sliver of latitude).
    for (const [p, q] of [[ia, ib], [ib, ic], [ic, ia]] as const) {
      const e = arcLatitudeExtremes(V, p, q);
      if (e.max > la1) la1 = e.max;
      if (e.min < la0) la0 = e.min;
    }
    // A little slack on top: the extremes are exact, the row quantisation is not.
    const y0 = Math.max(0, rowAtLat(la1) - 2);
    const y1 = Math.min(H - 1, rowAtLat(la0) + 2);
    if (y0 > H - 1 || y1 < 0) continue;
    const x0 = Math.floor((lonMin + Math.PI) / pxLon) - 1;
    const x1 = Math.ceil((lonMax + Math.PI) / pxLon) + 1;

    const hA = t.heights[ia], hB = t.heights[ib], hC = t.heights[ic];
    const ldA = lakeDepth?.[ia] ?? 0, ldB = lakeDepth?.[ib] ?? 0, ldC = lakeDepth?.[ic] ?? 0;
    const lA = ldA > 0 ? 1 : 0, lB = ldB > 0 ? 1 : 0, lC = ldC > 0 ? 1 : 0;
    const anyLake = lA + lB + lC > 0;
    const bA = t.biomes[ia], bB = t.biomes[ib], bC = t.biomes[ic];
    const sameBiome = bA === bB && bB === bC;
    for (let y = y0; y <= y1; y++) {
      const sl = rowSin[y], cl = rowCos[y];
      for (let xx = x0; xx <= x1; xx++) {
        const x = ((xx % W) + W) % W;
        const dx = cl * colCos[x], dy = cl * colSin[x], dz = sl;
        const wa = sgn * (dx * nax + dy * nay + dz * naz);
        if (wa < -1e-12) continue;
        const wb = sgn * (dx * nbx + dy * nby + dz * nbz);
        if (wb < -1e-12) continue;
        const wc = sgn * (dx * ncx + dy * ncy + dz * ncz);
        if (wc < -1e-12) continue;
        const s = wa + wb + wc;
        const i = y * W + x;
        elevation[i] = (wa * hA + wb * hB + wc * hC) / s;
        if (anyLake) {
          lakeShare![i] = (wa * lA + wb * lB + wc * lC) / s;
          lakeDepthPx![i] = (wa * ldA + wb * ldB + wc * ldC) / s;
          if (y < lakeY0) lakeY0 = y;
          if (y > lakeY1) lakeY1 = y;
        }
        tile[i] = wa >= wb && wa >= wc ? ia : wb >= wc ? ib : ic;
        if (sameBiome) { biome[i] = bA; continue; }
        const ja = wa / s + biomeJitter * (valueNoise(x * jf, y * jf, ia) - 0.5);
        const jb = wb / s + biomeJitter * (valueNoise(x * jf, y * jf, ib) - 0.5);
        const jc = wc / s + biomeJitter * (valueNoise(x * jf, y * jf, ic) - 0.5);
        biome[i] = ja >= jb && ja >= jc ? bA : jb >= jc ? bB : bC;
      }
    }
  }
  for (let i = 0; i < elevation.length; i++) if (Number.isNaN(elevation[i])) throw new Error(`raster hole at pixel ${i}`);
  if (lakeShare && lakeDepthPx && lakeY1 >= lakeY0) carveLakes(elevation, lakeShare, lakeDepthPx, W, H, lakeY0, lakeY1);
  const waterPixels = new Int32Array(nT);
  for (let i = 0; i < elevation.length; i++) if (elevation[i] < 0) waterPixels[tile[i]]++;
  return { width: W, height: H, elevation, biome, tile, waterPixels };
}
