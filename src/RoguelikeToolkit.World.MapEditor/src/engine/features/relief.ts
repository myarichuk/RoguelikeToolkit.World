import { MAP_H, MAP_W } from '../core/geo';
import { hash2, hashString, mulberry32 } from '../core/rng';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { PlacementRule } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import type { StampEntity } from '../doc/types';

/** Two relief stamps may not sit closer than this share of their summed half-widths (x) and half-heights (y). */
const GAP_X = 0.78, GAP_Y = 0.42;
const smoothstep = (a: number, b: number, v: number): number => { const t = Math.min(1, Math.max(0, (v - a) / (b - a))); return t * t * (3 - 2 * t); };

/** A placed relief stamp's body, in map units: visual center and size. */
export interface Footprint { x: number; y: number; w: number; h: number; }

/**
 * Footprints of placed relief stamps in a spatial hash (x wraps). Shared by every relief rule, so
 * hills fill in around mountains instead of under them, and used afterwards to keep scattered
 * vegetation off the hills' bodies.
 */
export class FootprintMap {
  private readonly cells = new Map<number, Footprint[]>();
  private readonly nx: number;
  private maxW = 0; private maxH = 0;
  constructor(private readonly cell = 16) { this.nx = Math.ceil(MAP_W / cell); }

  add(f: Footprint): void {
    const k = this.key(f.x, f.y);
    let l = this.cells.get(k); if (!l) this.cells.set(k, (l = []));
    l.push(f);
    this.maxW = Math.max(this.maxW, f.w); this.maxH = Math.max(this.maxH, f.h);
  }

  /** Whether a body of size (w, h) centered at (x, y) keeps the gap from every placed one. */
  fits(x: number, y: number, w: number, h: number): boolean {
    let ok = true;
    this.near(x, y, (w + this.maxW) * 0.5 * GAP_X, (h + this.maxH) * 0.5 * GAP_Y, (f, dx, dy) => {
      const ex = (w + f.w) * 0.5 * GAP_X, ey = (h + f.h) * 0.5 * GAP_Y;
      if ((dx / ex) ** 2 + (dy / ey) ** 2 < 1) ok = false;
    });
    return ok;
  }

  /** Whether (x, y) lies on the lower body of a placed stamp (where scattered small stamps would read as clutter). */
  covers(x: number, y: number): boolean {
    let hit = false;
    this.near(x, y, this.maxW * 0.5, this.maxH * 0.5, (f, dx, dy) => {
      if (Math.abs(dx) < f.w * 0.36 && dy > -f.h * 0.2 && dy < f.h * 0.42) hit = true;
    });
    return hit;
  }

  private key(x: number, y: number): number {
    const cx = ((Math.floor(x / this.cell) % this.nx) + this.nx) % this.nx;
    return Math.floor(y / this.cell) * this.nx + cx;
  }

  private near(x: number, y: number, rx: number, ry: number, fn: (f: Footprint, dx: number, dy: number) => void): void {
    const kx = Math.ceil(rx / this.cell), ky = Math.ceil(ry / this.cell);
    const bx = Math.floor(x / this.cell), by = Math.floor(y / this.cell);
    for (let j = by - ky; j <= by + ky; j++) {
      for (let i = bx - kx; i <= bx + kx; i++) {
        const l = this.cells.get(j * this.nx + (((i % this.nx) + this.nx) % this.nx));
        if (!l) continue;
        for (const f of l) {
          let dx = x - f.x; dx -= Math.round(dx / MAP_W) * MAP_W;
          fn(f, dx, y - f.y);
        }
      }
    }
  }
}

/** Smooth value noise in [0, 1], periodic in x over the map width. */
function valueNoise(seed: number, x: number, y: number, cell: number): number {
  const L = Math.max(1, Math.round(MAP_W / cell));
  const gx = (x / MAP_W) * L, gy = y / cell;
  const ix = Math.floor(gx), iy = Math.floor(gy), fx = gx - ix, fy = gy - iy;
  const v = (i: number, j: number) => hash2(seed, (((i % L) + L) % L) * 7919 + j * 104729) / 4294967296;
  const sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
  const a = v(ix, iy) + (v(ix + 1, iy) - v(ix, iy)) * sx;
  const b = v(ix, iy + 1) + (v(ix + 1, iy + 1) - v(ix, iy + 1)) * sx;
  return a + (b - a) * sy;
}

interface Candidate { cx: number; cy: number; x: number; y: number; h: number; prom: number; r: number[]; }

/**
 * Places one "relief" rule (see `PlacementRule.layout`). Candidates on a half-spacing grid are
 * scored by height and by prominence over a ring around them, then placed strongest first: size
 * follows that rank, and each keeps an elliptical gap from what is already placed (wide
 * across, narrow up and down, so a range overlaps back to front like a drawn chain). Deterministic
 * in (seed, rule id, cell); ids are stable per candidate cell.
 */
/** A stamp stands on its base: both ends of it and its body up to the crown must be dry ground, so a crown never hangs out over a lake or bay. */
export function baseOnLand(elev: (x: number, y: number) => number, x: number, y: number, w: number, h: number): boolean {
  const dx = w * 0.4;
  return elev(x - dx, y) >= 0.02 && elev(x + dx, y) >= 0.02 && elev(x - dx, y + 2) >= 0.02 && elev(x + dx, y + 2) >= 0.02 && elev(x, y - h * 0.4) >= 0.02 && elev(x - dx * 0.6, y - h * 0.8) >= 0.02 && elev(x + dx * 0.6, y - h * 0.8) >= 0.02;
}

export function placeRelief(doc: MapDocument, rule: PlacementRule, seed: number, library: AssetLibrary, density: number, occupied: FootprintMap): StampEntity[] {
  const usable = (list: string[]) => list.filter((a) => library.variants(a) > 0);
  const base = usable(rule.assets);
  const tiers = (rule.tiers ?? []).map((t) => ({ above: t.above, assets: usable(t.assets) })).filter((t) => t.assets.length).sort((a, b) => b.above - a.above);
  const ref = library.get(base[0] ?? tiers[0]?.assets[0] ?? '', 0);
  if (!ref) return [];
  const ruleSeed = hash2(seed, hashString(rule.id));
  const [emin, emax] = rule.elevation ?? [0.3, 99];
  const biomes = rule.biomes ? new Set(rule.biomes) : null;
  const ridge = Math.min(1, Math.max(0, rule.ridge ?? 0.5));
  const clump = Math.min(1, Math.max(0, rule.clump ?? 0));
  const [s0, s1] = rule.scale ?? [0.6, 1.4];
  const elev = (x: number, y: number) => doc.elevationAt(x, y);

  // Candidates, one per half-spacing cell.
  const step = rule.spacing * 0.5, R = rule.spacing * 1.6;
  const nx = Math.floor(MAP_W / step), ny = Math.floor(MAP_H / step);
  const cands: Candidate[] = [];
  for (let cy = 0; cy < ny; cy++) {
    for (let cx = 0; cx < nx; cx++) {
      const rng = mulberry32(hash2(ruleSeed, cy * 65536 + cx));
      const r = [rng(), rng(), rng(), rng(), rng(), rng(), rng(), rng()];
      const x = (cx + 0.1 + r[0]! * 0.8) * step, y = (cy + 0.1 + r[1]! * 0.8) * step;
      const h = elev(x, y);
      if (h < emin || h > emax) continue;
      if (Math.min(elev(x - 2, y), elev(x + 2, y), elev(x, y + 2)) < 0.02) continue;
      if (biomes && !biomes.has(doc.biomeAt(x, y))) continue;
      let ring = 0;
      for (let k = 0; k < 8; k++) { const a = (k / 8) * Math.PI * 2; ring += elev(x + Math.cos(a) * R, y + Math.sin(a) * R); }
      cands.push({ cx, cy, x, y, h, prom: h - ring / 8, r });
    }
  }
  if (!cands.length) return [];

  // Normalizers: height across the band actually present; prominence against its 90th percentile.
  let hmax = emin;
  for (const c of cands) hmax = Math.max(hmax, c.h);
  const proms = cands.map((c) => Math.abs(c.prom)).sort((a, b) => a - b);
  const p90 = Math.max(1e-4, proms[Math.floor(proms.length * 0.9)]!);
  const span = Math.max(1e-4, hmax - emin);
  const clumpSeed = hash2(ruleSeed, 0x51ed);
  const scored: { c: Candidate; score: number; keep: number }[] = [];
  for (const c of cands) {
    const pn = Math.max(-1, Math.min(1, c.prom / p90));
    const ht = Math.min(1, (c.h - emin) / span);
    let keep = Math.min(1, rule.density * density);
    keep *= 1 - ridge + ridge * smoothstep(-0.7, 0.15, pn);           // valleys open up
    if (clump > 0) {
      const n = valueNoise(clumpSeed, c.x, c.y, rule.spacing * 6) * 0.75 + valueNoise(clumpSeed + 1, c.x, c.y, rule.spacing * 2) * 0.25;
      keep *= smoothstep(clump - 0.08, clump + 0.08, n);  // clumps and clearings: clump ~ share of open ground
    }
    scored.push({ c, keep, score: (1 - ridge) * ht + ridge * smoothstep(-0.4, 0.9, pn) + (c.r[3]! - 0.5) * 0.1 });
  }
  scored.sort((a, b) => b.score - a.score || a.c.cy - b.c.cy || a.c.cx - b.c.cx);

  // Size follows rank, not raw score, so even a broad plateau gets a few big peaks and many small
  // ones. A slot is claimed before the density roll: a dropped stamp leaves its ground empty
  // instead of letting a neighbour pack in, so density and clumping read as open space.
  const out: StampEntity[] = [];
  const N = scored.length;
  scored.forEach(({ c, keep }, i) => {
    const tier = tiers.find((t) => c.h >= t.above);
    const assets = tier ? tier.assets : base;
    if (!assets.length) return;
    const asset = assets[Math.floor(c.r[4]! * assets.length)]!;
    const n = library.variants(asset);
    const variant = Math.floor(c.r[5]! * n * 997) % n;
    const e = library.get(asset, variant) ?? ref;
    const rank = 1 - i / N;
    const scale = s0 + (s1 - s0) * Math.min(1, rank ** 1.8 * 0.85 + c.r[6]! * 0.15);
    const w = e.w * scale, h = e.h * scale;
    const vy = c.y - h * (e.ay - 0.5);
    if (!baseOnLand(elev, c.x, c.y, w, h)) return;
    if (!occupied.fits(c.x, vy, w, h)) return;
    occupied.add({ x: c.x, y: vy, w, h });
    if (c.r[2]! >= keep) return;
    out.push({
      id: `st_${rule.id}_${c.cx}_${c.cy}`, asset, variant, x: c.x, y: c.y, scale,
      rotation: (c.r[7]! - 0.5) * 0.06, flip: (c.r[7]! * 997) % 1 < 0.5,
    });
  });
  return out;
}
