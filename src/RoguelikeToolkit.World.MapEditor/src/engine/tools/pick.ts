import { MAP_W } from '../core/geo';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { Theme } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, StampEntity } from '../doc/types';
import { existsAt } from '../world/history/timeline';
import type { ViewSettings } from '../render/ViewSettings';
import type { LabelCache } from '../text/LabelCache';

const wrapD = (d: number) => d - Math.round(d / MAP_W) * MAP_W;

export function featureVisible(f: FeatureEntity, view: ViewSettings): boolean {
  if (!view.kinds[f.kind] || !existsAt(f.span, view.historyTurn)) return false;
  if (f.kind === 'deposit') return view.layers.resources && !!f.deposit && view.deposits[f.deposit.type];
  return true;
}

/**
 * The feature under a map point, honoring filters: point features (settlements, deposits) and
 * rivers win within a few screen pixels; otherwise the smallest visible area containing the
 * point (a lake inside a forest inside a continent picks the lake).
 */
export function pickFeature(doc: MapDocument, x: number, y: number, zoom: number, view: ViewSettings): FeatureEntity | null {
  const tol = 9 / zoom;
  let best: FeatureEntity | null = null, bd = tol * tol;
  for (const f of doc.features.all()) {
    if (f.tiles || !featureVisible(f, view)) continue;
    const d = wrapD(f.x - x) ** 2 + (f.y - y) ** 2;
    if (d < bd) { bd = d; best = f; }
  }
  if (best) return best;
  if (view.kinds.river) {
    const rtol = Math.max(1.5, 6 / zoom);
    let bestR: FeatureEntity | null = null, rd = rtol;
    for (const f of doc.features.all()) {
      if (f.kind !== 'river' || !f.riverId) continue;
      const r = doc.rivers.get(f.riverId);
      if (!r) continue;
      const d = polylineDistance(r.points, 3, x, y);
      if (d < rd) { rd = d; bestR = f; }
    }
    if (bestR) return bestR;
  }
  let area: FeatureEntity | null = null;
  for (const f of doc.featuresAt(x, y)) {
    if (!featureVisible(f, view)) continue;
    if (!area || (f.tiles?.length ?? 0) < (area.tiles?.length ?? 0)) area = f;
  }
  return area;
}

/** Distance from a point to a polyline stored as flat tuples (stride 2 or 3), wrap-aware. */
export function polylineDistance(p: ArrayLike<number>, stride: number, x: number, y: number): number {
  let best = Infinity;
  for (let i = 0; i + stride < p.length; i += stride) {
    const ax = p[i], ay = p[i + 1], bx = p[i + stride], by = p[i + stride + 1];
    const qx = ax + wrapD(x - ax);
    const dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy || 1e-9;
    const t = Math.max(0, Math.min(1, ((qx - ax) * dx + (y - ay) * dy) / len2));
    best = Math.min(best, Math.hypot(ax + t * dx - qx, ay + t * dy - y));
  }
  return best;
}

/** Topmost stamp whose box contains the point (stamps paint in y order: largest y on top). */
export function pickStamp(doc: MapDocument, lib: AssetLibrary, x: number, y: number, visible: (id: string) => boolean = () => true): StampEntity | null {
  let best: StampEntity | null = null;
  for (const s of doc.stamps.all()) {
    if (!visible(s.id)) continue;
    const e = lib.get(s.asset, s.variant);
    if (!e) continue;
    const w = e.w * s.scale, h = e.h * s.scale;
    const lx = wrapD(x - s.x), ly = y - s.y;
    if (lx < -e.ax * w || lx > (1 - e.ax) * w || ly < -e.ay * h || ly > (1 - e.ay) * h) continue;
    if (!best || s.y >= best.y) best = s;
  }
  return best;
}

export function stampBounds(s: StampEntity, lib: AssetLibrary): { x0: number; y0: number; x1: number; y1: number } | null {
  const e = lib.get(s.asset, s.variant);
  if (!e) return null;
  const w = e.w * s.scale, h = e.h * s.scale;
  return { x0: s.x - e.ax * w, y0: s.y - e.ay * h, x1: s.x + (1 - e.ax) * w, y1: s.y + (1 - e.ay) * h };
}

export function pickLabel(doc: MapDocument, labels: LabelCache, theme: Theme, x: number, y: number, visible: (id: string) => boolean): string | null {
  for (const l of doc.labels.all()) {
    if (!visible(l.id)) continue;
    const r = labels.resolve(l, theme);
    if (!r) continue;
    const b = r.geo.bounds, qx = b.x0 + wrapD(x - b.x0);
    if (qx >= b.x0 && qx <= b.x1 && y >= b.y0 && y <= b.y1) return l.id;
  }
  return null;
}
