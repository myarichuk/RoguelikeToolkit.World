import { MAP_H, MAP_W } from '../core/geo';
import { hash2, hashString, mulberry32 } from '../core/rng';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { PlacementRule } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, StampEntity } from '../doc/types';

export interface PopulateOptions {
  seed: number;
  rules: PlacementRule[];
  library: AssetLibrary;
  /** Feature kind -> asset ids for settlement markers. */
  featureAssets: (kind: string) => string[];
  /** Density multiplier, 0..2. */
  density?: number;
}

/**
 * Rule-driven scatter: one jittered candidate per grid cell (spacing = cell size), kept with the
 * rule's density where elevation/biome fit. Deterministic in (seed, rule id, cell). Settlements
 * then get their markers and clear vegetation around them. Stable ids derive from the rule and
 * cell, so re-running yields the same stamps.
 */
export function populate(doc: MapDocument, o: PopulateOptions): StampEntity[] {
  const out: StampEntity[] = [];
  const density = o.density ?? 1;
  const elevAt = (x: number, y: number) => doc.elevationAt(x, y);
  for (const rule of o.rules) {
    const assets = rule.assets.filter((a) => o.library.variants(a) > 0);
    if (!assets.length) continue;
    const ruleSeed = hash2(o.seed, hashString(rule.id));
    const [emin, emax] = rule.elevation ?? [0.02, 99];
    const land = emin >= 0;
    const biomes = rule.biomes ? new Set(rule.biomes) : null;
    const s = rule.spacing;
    const nx = Math.floor(MAP_W / s), ny = Math.floor(MAP_H / s);
    for (let cy = 0; cy < ny; cy++) {
      for (let cx = 0; cx < nx; cx++) {
        const rng = mulberry32(hash2(ruleSeed, cy * 65536 + cx));
        if (rng() > rule.density * density) continue;
        const x = (cx + 0.15 + rng() * 0.7) * s, y = (cy + 0.15 + rng() * 0.7) * s;
        const h = elevAt(x, y);
        if (h < emin || h > emax) continue;
        // Keep land stamps off the (warped) coastline and sea stamps away from it.
        if (land && Math.min(elevAt(x - 2, y), elevAt(x + 2, y), elevAt(x, y + 2)) < 0.02) continue;
        if (!land && Math.max(elevAt(x - 6, y), elevAt(x + 6, y), elevAt(x, y - 6), elevAt(x, y + 6)) > -0.04) continue;
        if (biomes && !biomes.has(doc.biomeAt(x, y))) continue;
        const asset = assets[Math.floor(rng() * assets.length)];
        const [s0, s1] = rule.scale ?? [0.85, 1.15];
        let scale = s0 + (s1 - s0) * rng();
        if (rule.scaleWithElevation !== undefined) scale *= 1 + Math.max(0, h - rule.scaleWithElevation) * 1.1;
        out.push({
          id: `st_${rule.id}_${cx}_${cy}`, asset, variant: Math.floor(rng() * 1e6) % Math.max(1, o.library.variants(asset)),
          x, y, scale, rotation: 0, flip: rng() < 0.5 && !/compass/.test(asset),
        });
      }
    }
  }
  // Settlement markers; their footprint clears trees and tufts.
  const clear: { x: number; y: number; r: number }[] = [];
  const settlements: StampEntity[] = [];
  for (const f of doc.features.all()) {
    const marker = settlementAsset(f, o.featureAssets);
    if (!marker || o.library.variants(marker) === 0) continue;
    const e = o.library.get(marker, 0)!;
    const variant = hash2(o.seed, hashString(f.id)) % o.library.variants(marker);
    settlements.push({ id: `st_${f.id}`, asset: marker, variant, x: f.x, y: f.y + e.h * (1 - e.ay) * 0.2, scale: 1, rotation: 0, flip: false });
    clear.push({ x: f.x, y: f.y - e.h * 0.3, r: Math.max(e.w, e.h) * 0.62 });
  }
  const kept = out.filter((st) => {
    if (/mountain|volcano|wave|ship|monster|whirlpool/i.test(st.asset)) return true;
    for (const c of clear) {
      let dx = st.x - c.x; dx -= Math.round(dx / MAP_W) * MAP_W;
      if (dx * dx + (st.y - c.y) ** 2 < c.r * c.r) return false;
    }
    return true;
  });
  return kept.concat(settlements);
}

function settlementAsset(f: FeatureEntity, featureAssets: (kind: string) => string[]): string | null {
  if (!['capital', 'city', 'town', 'village'].includes(f.kind)) return null;
  const coastal = f.details.some(([k, v]) => k === 'Water access' && v.includes('coast'));
  const port = featureAssets('port');
  if (coastal && f.kind === 'town' && port.length) return port[0];
  const list = featureAssets(f.kind);
  return list.length ? list[0] : null;
}
