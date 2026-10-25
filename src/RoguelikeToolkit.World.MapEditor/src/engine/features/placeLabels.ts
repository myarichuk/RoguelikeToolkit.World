import { MAP_W, type Rect } from '../core/geo';
import { hash2, hashString } from '../core/rng';
import type { Theme } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import { labelIdOf, type FeatureEntity, type LabelEntity } from '../doc/types';
import type { FontLibrary } from '../text/FontLibrary';
import { layoutLabel } from '../text/LabelLayout';

/** Placement priority: big, important names claim space first. */
const PRIORITY: Record<string, number> = {
  ocean: 0, capital: 1, sea: 2, city: 3, mountain: 4, forest: 5, lake: 6, town: 7, river: 8, valley: 9, village: 10, fort: 11, shrine: 12, ruin: 13, battlefield: 14,
};

/**
 * Greedy cartographic label placement. Each feature proposes candidates (settlements: the four
 * sides of the marker; areas: the anchor with a gentle arc along the principal axis; rivers: the
 * middle stretch of the river as a baseline path). Candidates are laid out with the real font,
 * and the first one whose bounds clear everything already placed wins. Wrap-aware.
 */
/** Samples map elevation at a point (map units), e.g. `(x, y) => doc.elevationAt(x, y)`. */
export type ElevationSampler = (x: number, y: number) => number;

/**
 * Share of an axis-aligned label footprint reading as land (elevation >= 0), sampled on an
 * n×n grid of cell centers. Uniform water reads 0, uniform land reads 1, independent of n.
 */
export function labelLandFraction(sample: ElevationSampler, bounds: Rect, n: number): number {
  const m = Math.max(1, Math.floor(n));
  const w = bounds.x1 - bounds.x0, h = bounds.y1 - bounds.y0;
  if (!(w > 0 && h > 0)) return sample((bounds.x0 + bounds.x1) / 2, (bounds.y0 + bounds.y1) / 2) >= 0 ? 1 : 0;
  let land = 0;
  for (let iy = 0; iy < m; iy++) {
    const y = bounds.y0 + ((iy + 0.5) / m) * h;
    for (let ix = 0; ix < m; ix++) {
      if (sample(bounds.x0 + ((ix + 0.5) / m) * w, y) >= 0) land++;
    }
  }
  return land / (m * m);
}

/** A sea/ocean candidate is usable only while it reads as mostly water. */
const MAX_SEA_LAND_FRACTION = 0.4;

export function placeLabels(doc: MapDocument, theme: Theme, fonts: FontLibrary, seed: number): LabelEntity[] {
  const feats = [...doc.features.all()].filter((f) => f.kind !== 'deposit' && f.name && PRIORITY[f.kind] !== undefined);
  // Weight normalization per kind (area labels grow with the feature).
  const maxW = new Map<string, number>();
  for (const f of feats) maxW.set(f.kind, Math.max(maxW.get(f.kind) ?? 0, f.weight));
  feats.sort((a, b) => PRIORITY[a.kind] - PRIORITY[b.kind] || b.weight - a.weight);

  const placed: Rect[] = [];
  const out: LabelEntity[] = [];
  const free = (r: Rect) => {
    for (const p of placed) {
      for (const off of [-MAP_W, 0, MAP_W]) {
        if (r.x0 + off < p.x1 && r.x1 + off > p.x0 && r.y0 < p.y1 && r.y1 > p.y0) return false;
      }
    }
    return true;
  };
  // Settlement markers are obstacles too.
  for (const s of doc.stamps.all()) {
    if (!/capital|city|town|village|port|fort/.test(s.asset)) continue;
    placed.push({ x0: s.x - 3, y0: s.y - 6, x1: s.x + 3, y1: s.y });
  }

  for (const f of feats) {
    const style = theme.labels[f.kind];
    const font = style && fonts.get(style.font);
    if (!style || !font) continue;
    const rel = f.weight / (maxW.get(f.kind) || 1);
    const size = style.size * (style.sizeExp ? Math.max(0.6, Math.pow(rel, style.sizeExp)) : 1);
    const textWidth = f.kind === 'river' ? layoutLabel(font, { text: f.name, size, spacing: style.spacing, uppercase: style.uppercase, x: 0, y: 0, angle: 0, curve: 0 }).width : 0;
    for (const cand of candidates(f, size, doc, seed, textWidth)) {
      const geo = layoutLabel(font, { text: f.name, size, spacing: style.spacing, uppercase: style.uppercase, ...cand });
      // Sea and ocean names belong on water: refuse a candidate sprawling over land so a
      // shifted fallback over open water wins. No usable water candidate means no label.
      if ((f.kind === 'sea' || f.kind === 'ocean') &&
        labelLandFraction((x, y) => doc.elevationAt(x, y), geo.bounds, 4) > MAX_SEA_LAND_FRACTION) continue;
      const pad = size * 0.25;
      const r = { x0: geo.bounds.x0 - pad, y0: geo.bounds.y0 - pad, x1: geo.bounds.x1 + pad, y1: geo.bounds.y1 + pad };
      // Area labels must stay roughly within their feature's extent.
      if (f.extent && (f.kind === 'lake' || f.kind === 'forest' || f.kind === 'valley') && geo.width > f.extent * 3.2) continue;
      if (!free(r)) continue;
      placed.push(r);
      out.push({
        id: labelIdOf(f.id), text: f.name, style: f.kind, x: cand.x, y: cand.y, angle: cand.angle, size, spacing: NaN, curve: cand.curve,
        path: cand.path, feature: { kind: f.kind, request: f.request },
      });
      break;
    }
  }
  return out;
}

interface Candidate { x: number; y: number; angle: number; curve: number; path?: number[]; }

function candidates(f: FeatureEntity, size: number, doc: MapDocument, seed: number, textWidth: number): Candidate[] {
  const bend = ((hash2(seed, hashString(f.id)) % 2) ? 1 : -1);
  switch (f.kind) {
    case 'capital': case 'city': case 'town': case 'village': case 'fort': case 'ruin': case 'shrine': case 'battlefield': {
      const g = size * 0.9;
      return [
        { x: f.x, y: f.y + g * 1.2, angle: 0, curve: 0 },
        { x: f.x, y: f.y - g * 3.2, angle: 0, curve: 0 },
      ].concat(sideCandidates(f, size));
    }
    case 'river': {
      const r = f.riverId ? doc.rivers.get(f.riverId) : undefined;
      return r ? riverCandidates(r.points, size, textWidth) : [];
    }
    case 'lake':
      return [{ x: f.x, y: f.y, angle: f.angle * 0.5, curve: 0 }, { x: f.x, y: f.y + size * 2.2, angle: 0, curve: 0 }];
    default: {
      // Areas: along the principal axis, gently arched; then flatter and shifted fallbacks.
      const a = f.angle * 0.7;
      const curve = f.kind === 'ocean' || f.kind === 'sea' || f.kind === 'mountain' ? 0.28 * bend : 0.12 * bend;
      return [
        { x: f.x, y: f.y, angle: a, curve },
        { x: f.x, y: f.y, angle: 0, curve: curve * 0.5 },
        { x: f.x, y: f.y - size * 2, angle: 0, curve: 0 },
        { x: f.x, y: f.y + size * 2, angle: 0, curve: 0 },
      ];
    }
  }
}

/**
 * Stretches of a river (x, y, w triples) long enough to carry its name, straightest first, with a
 * mild preference for the middle of the river. A stretch is the name's width plus a cap height
 * either side; a river too short for its name gets no label rather than one hanging off its ends.
 */
export function riverCandidates(p: ArrayLike<number>, size: number, textWidth: number): Candidate[] {
  const n = p.length / 3;
  if (n < 2) return [];
  const cum = [0];
  for (let i = 1; i < n; i++) cum.push(cum[i - 1] + Math.hypot(p[i * 3] - p[i * 3 - 3], p[i * 3 + 1] - p[i * 3 - 2]));
  const total = cum[n - 1], half = textWidth / 2 + size;
  if (total < half * 2) return [];
  const idx = (s: number) => { let i = 0; while (i < n - 1 && cum[i] < s) i++; return i; };
  const out: { c: Candidate; score: number }[] = [];
  for (let k = 1; k < 12; k++) {
    const mid = (k / 12) * total;
    if (mid - half < 0 || mid + half > total) continue;
    const i0 = idx(mid - half), i1 = idx(mid + half), ic = idx(mid);
    const path: number[] = [];
    for (let i = i0; i <= i1; i++) path.push(p[i * 3], p[i * 3 + 1]);
    const chord = Math.hypot(p[i1 * 3] - p[i0 * 3], p[i1 * 3 + 1] - p[i0 * 3 + 1]);
    const straight = chord / Math.max(1e-6, cum[i1] - cum[i0]);
    out.push({ c: { x: p[ic * 3], y: p[ic * 3 + 1], angle: 0, curve: 0, path }, score: straight - 0.25 * Math.abs(k / 12 - 0.5) });
  }
  return out.sort((a, b) => b.score - a.score).slice(0, 5).map((o) => o.c);
}

/** Left/right of a settlement marker (text centered on the anchor: shift by half its width later). */
function sideCandidates(f: FeatureEntity, size: number): Candidate[] {
  const w = f.name.length * size * 0.36 + size;
  return [
    { x: f.x + w, y: f.y - size * 0.8, angle: 0, curve: 0 },
    { x: f.x - w, y: f.y - size * 0.8, angle: 0, curve: 0 },
  ];
}
