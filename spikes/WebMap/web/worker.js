// Runs RoguelikeToolkit.World.Core (browser-wasm) off the main thread, then rasterizes the
// icosphere fields to an equirectangular grid. The sim hands over its native tile graph;
// turning that into pixels is the map tool's job, not Core's.
import { dotnet } from './_framework/dotnet.js';

let exportsPromise = null;
async function engine() {
  exportsPromise ??= (async () => {
    const t0 = performance.now();
    const runtime = await dotnet.create();
    const config = runtime.getConfig();
    const asm = await runtime.getAssemblyExports(config.mainAssemblyName);
    return { api: asm.WebMap.Engine.WorldExports, bootMs: performance.now() - t0 };
  })();
  return exportsPromise;
}

self.onmessage = async (e) => {
  const { seed, size, width, height } = e.data;
  try {
    const { api, bootMs } = await engine();
    let t = performance.now();
    const tiles = api.Generate(seed, size);
    const genMs = performance.now() - t;

    t = performance.now();
    const vectors = api.TileVectors();
    const faces = api.Faces();
    const heights = api.Heights();
    const biomes = api.Biomes();
    const pullMs = performance.now() - t;

    t = performance.now();
    const raster = rasterize(vectors, faces, heights, biomes, width, height);
    const rasterMs = performance.now() - t;

    self.postMessage(
      { ok: true, tiles, width, height, heights: raster.h, biomes: raster.b,
        timings: { bootMs, genMs, pullMs, rasterMs } },
      [raster.h.buffer, raster.b.buffer]);
  } catch (err) {
    self.postMessage({ ok: false, error: String(err && err.stack || err) });
  }
};

// Parchment-tinted biome colors, indexed by Core's BiomeType.
const BIOME_RGB = [
  [204, 217, 212], [222, 212, 166], [235, 214, 163], [171, 186, 140], [194, 184, 163],
  [219, 219, 207], [148, 171, 125], [161, 171, 143], [242, 242, 240], [217, 168, 128]];

// Forward rasterization of spherical triangles. For a pixel direction d, the weights
// (d·(b×c), d·(c×a), d·(a×b)) are all non-negative iff the ray hits triangle abc, and,
// normalized, are the barycentric coordinates of that hit: heights interpolate linearly
// across each face instead of snapping to the nearest tile (no hex staircase in the coast).
// Biome colors blend the same way, so biome borders are soft gradients, not hexagons.
function rasterize(V, F, H, B, W, Hh) {
  const h = new Float32Array(W * Hh).fill(NaN);
  const b = new Uint8Array(W * Hh * 4);
  const lon = new Float64Array(V.length / 3), lat = new Float64Array(V.length / 3);
  for (let i = 0; i < lon.length; i++) {
    lon[i] = Math.atan2(V[i * 3 + 1], V[i * 3]);
    lat[i] = Math.asin(Math.max(-1, Math.min(1, V[i * 3 + 2])));
  }
  const pxLon = (2 * Math.PI) / W, pxLat = Math.PI / Hh;
  // Precomputed per-row trig.
  const rowSin = new Float64Array(Hh), rowCos = new Float64Array(Hh);
  for (let y = 0; y < Hh; y++) {
    const la = Math.PI / 2 - (y + 0.5) * pxLat;
    rowSin[y] = Math.sin(la); rowCos[y] = Math.cos(la);
  }
  const colSin = new Float64Array(W), colCos = new Float64Array(W);
  for (let x = 0; x < W; x++) {
    const lo = -Math.PI + (x + 0.5) * pxLon;
    colSin[x] = Math.sin(lo); colCos[x] = Math.cos(lo);
  }

  for (let f = 0; f < F.length; f += 3) {
    const ia = F[f], ib = F[f + 1], ic = F[f + 2];
    const ax = V[ia * 3], ay = V[ia * 3 + 1], az = V[ia * 3 + 2];
    const bx = V[ib * 3], by = V[ib * 3 + 1], bz = V[ib * 3 + 2];
    const cx = V[ic * 3], cy = V[ic * 3 + 1], cz = V[ic * 3 + 2];
    // Edge normals: na = b×c, nb = c×a, nc = a×b.
    const nax = by * cz - bz * cy, nay = bz * cx - bx * cz, naz = bx * cy - by * cx;
    const nbx = cy * az - cz * ay, nby = cz * ax - cx * az, nbz = cx * ay - cy * ax;
    const ncx = ay * bz - az * by, ncy = az * bx - ax * bz, ncz = ax * by - ay * bx;
    // Orientation: make the weights positive inside regardless of winding.
    const sgn = Math.sign(ax * nax + ay * nay + az * naz) || 1;

    let lo0 = lon[ia], lo1 = lon[ib], lo2 = lon[ic];
    let la0 = Math.min(lat[ia], lat[ib], lat[ic]), la1 = Math.max(lat[ia], lat[ib], lat[ic]);
    let lonMin, lonMax;
    const containsPole = (px, py, pz) =>
      sgn * (px * nax + py * nay + pz * naz) >= 0 &&
      sgn * (px * nbx + py * nby + pz * nbz) >= 0 &&
      sgn * (px * ncx + py * ncy + pz * ncz) >= 0;
    if (containsPole(0, 0, 1)) { la1 = Math.PI / 2; lonMin = -Math.PI; lonMax = Math.PI; }
    else if (containsPole(0, 0, -1)) { la0 = -Math.PI / 2; lonMin = -Math.PI; lonMax = Math.PI; }
    else {
      if (Math.max(lo0, lo1, lo2) - Math.min(lo0, lo1, lo2) > Math.PI) {
        // Straddles the antimeridian: unwrap negatives, rasterize past +pi, wrap x.
        if (lo0 < 0) lo0 += 2 * Math.PI;
        if (lo1 < 0) lo1 += 2 * Math.PI;
        if (lo2 < 0) lo2 += 2 * Math.PI;
      }
      lonMin = Math.min(lo0, lo1, lo2); lonMax = Math.max(lo0, lo1, lo2);
    }
    // Great-circle edges bulge poleward of their endpoints' latitude range; pad by a pixel.
    const y0 = Math.max(0, Math.floor((Math.PI / 2 - la1) / pxLat) - 1);
    const y1 = Math.min(Hh - 1, Math.ceil((Math.PI / 2 - la0) / pxLat) + 1);
    const x0 = Math.floor((lonMin + Math.PI) / pxLon) - 1;
    const x1 = Math.ceil((lonMax + Math.PI) / pxLon) + 1;

    const hA = H[ia], hB = H[ib], hC = H[ic];
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
        h[i] = (wa * hA + wb * hB + wc * hC) / s;
        const ca = BIOME_RGB[B[ia]] || BIOME_RGB[0], cb = BIOME_RGB[B[ib]] || BIOME_RGB[0], cc = BIOME_RGB[B[ic]] || BIOME_RGB[0];
        for (let k = 0; k < 3; k++) b[i * 4 + k] = (wa * ca[k] + wb * cb[k] + wc * cc[k]) / s;
        b[i * 4 + 3] = 255;
      }
    }
  }
  for (let i = 0; i < h.length; i++) if (Number.isNaN(h[i])) throw new Error(`raster hole at pixel ${i}`);
  return { h, b };
}
