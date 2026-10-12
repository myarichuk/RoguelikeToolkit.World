import { MAP_W, type Rect } from '../core/geo';
import { hash2, hashString } from '../core/rng';
import type { Theme } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, LabelEntity } from '../doc/types';
import type { FontLibrary } from '../text/FontLibrary';
import { layoutLabel } from '../text/LabelLayout';

/** Placement priority: big, important names claim space first. */
const PRIORITY: Record<string, number> = {
  ocean: 0, capital: 1, sea: 2, city: 3, mountain: 4, forest: 5, lake: 6, town: 7, river: 8, valley: 9, village: 10,
};

/**
 * Greedy cartographic label placement. Each feature proposes candidates (settlements: the four
 * sides of the marker; areas: the anchor with a gentle arc along the principal axis; rivers: the
 * middle stretch of the river as a baseline path). Candidates are laid out with the real font,
 * and the first one whose bounds clear everything already placed wins. Wrap-aware.
 */
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
    if (!/capital|city|town|village|port/.test(s.asset)) continue;
    placed.push({ x0: s.x - 3, y0: s.y - 6, x1: s.x + 3, y1: s.y });
  }

  for (const f of feats) {
    const style = theme.labels[f.kind];
    const font = style && fonts.get(style.font);
    if (!style || !font) continue;
    const rel = f.weight / (maxW.get(f.kind) || 1);
    const size = style.size * (style.sizeExp ? Math.max(0.6, Math.pow(rel, style.sizeExp)) : 1);
    for (const cand of candidates(f, size, doc, seed)) {
      const geo = layoutLabel(font, { text: f.name, size, spacing: style.spacing, uppercase: style.uppercase, ...cand });
      const pad = size * 0.25;
      const r = { x0: geo.bounds.x0 - pad, y0: geo.bounds.y0 - pad, x1: geo.bounds.x1 + pad, y1: geo.bounds.y1 + pad };
      // Area labels must stay roughly within their feature's extent.
      if (f.extent && (f.kind === 'lake' || f.kind === 'forest' || f.kind === 'valley') && geo.width > f.extent * 3.2) continue;
      if (!free(r)) continue;
      placed.push(r);
      out.push({
        id: `lb_${f.id}`, text: f.name, style: f.kind, x: cand.x, y: cand.y, angle: cand.angle, size, spacing: NaN, curve: cand.curve,
        path: cand.path, feature: { kind: f.kind, request: f.request },
      });
      break;
    }
  }
  return out;
}

interface Candidate { x: number; y: number; angle: number; curve: number; path?: number[]; }

function candidates(f: FeatureEntity, size: number, doc: MapDocument, seed: number): Candidate[] {
  const bend = ((hash2(seed, hashString(f.id)) % 2) ? 1 : -1);
  switch (f.kind) {
    case 'capital': case 'city': case 'town': case 'village': {
      const g = size * 0.9;
      return [
        { x: f.x, y: f.y + g * 1.2, angle: 0, curve: 0 },
        { x: f.x, y: f.y - g * 3.2, angle: 0, curve: 0 },
      ].concat(sideCandidates(f, size));
    }
    case 'river': {
      const r = f.riverId ? doc.rivers.get(f.riverId) : undefined;
      if (!r || r.points.length < 12) return [];
      const n = r.points.length / 3;
      const out: Candidate[] = [];
      for (const centre of [0.5, 0.35, 0.65, 0.25, 0.75]) {
        const c = Math.floor(n * centre), span = Math.max(4, Math.floor(n * 0.22));
        const path: number[] = [];
        for (let i = Math.max(0, c - span); i <= Math.min(n - 1, c + span); i++) path.push(r.points[i * 3], r.points[i * 3 + 1]);
        out.push({ x: r.points[c * 3], y: r.points[c * 3 + 1], angle: 0, curve: 0, path });
      }
      return out;
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

/** Left/right of a settlement marker (text centered on the anchor: shift by half its width later). */
function sideCandidates(f: FeatureEntity, size: number): Candidate[] {
  const w = f.name.length * size * 0.36 + size;
  return [
    { x: f.x + w, y: f.y - size * 0.8, angle: 0, curve: 0 },
    { x: f.x - w, y: f.y - size * 0.8, angle: 0, curve: 0 },
  ];
}
