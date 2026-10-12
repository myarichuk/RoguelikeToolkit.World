// WKT export of the document as the author sees it: hand-drawn rivers are in, deleted ones are not,
// every name is the one on the map. Output is a "WKT CSV" (a `wkt` column plus attributes), which
// QGIS, GDAL (OGR CSV driver) and PostGIS (ST_GeomFromText) read directly. Coordinates are
// longitude/latitude in degrees (EPSG:4326, WKT axis order lon lat), inverted from the map's Mercator.
//
// What is exported, honestly: rivers are lines (the drawn, meandered polylines, split where they cross
// the antimeridian so no segment spans the planet); settlements and deposits are points; areas (seas,
// lakes, ranges, valleys, forests) are their label anchor point with their size, because the document
// stores them as tile membership, not as an outline. Core's own World.*ToWkt gives tile-chain geometry
// for hosts that run the engine directly.
import { MAP_W, kmPerMu, mapToLonLatDeg } from '../core/geo';
import { isGeneratedFeatureId, type MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, RiverEntity } from '../doc/types';

const AREA_KINDS = new Set<FeatureEntity['kind']>(['lake', 'sea', 'ocean', 'mountain', 'valley', 'forest']);

const num = (v: number): string => (Math.abs(v) < 5e-7 ? 0 : v).toFixed(6);
const pt = (lon: number, lat: number): string => `${num(lon)} ${num(lat)}`;

function lonLat(x: number, y: number): [number, number] {
  const { lon, lat } = mapToLonLatDeg(x, y);
  return [lon, lat];
}

/**
 * A river polyline (x, y, width triples, x unwrapped) as WKT lon/lat. Where it crosses the seam of the
 * wrapped map the line is cut at ±180° and becomes a MULTILINESTRING, interpolating the crossing.
 */
export function riverToWkt(points: ArrayLike<number>): string {
  const n = Math.floor(points.length / 3);
  if (n < 2) return '';
  const parts: string[][] = [[]];
  let k = Math.floor(points[0] / MAP_W);
  const first = lonLat(points[0], points[1]);
  parts[0].push(pt(first[0], first[1]));
  for (let i = 1; i < n; i++) {
    const xa = points[(i - 1) * 3], ya = points[(i - 1) * 3 + 1];
    const xb = points[i * 3], yb = points[i * 3 + 1];
    const kb = Math.floor(xb / MAP_W);
    // Every seam between the two points, in travel order: a ±180° endpoint closes a part, the opposite one opens the next.
    const east = kb > k;
    for (let s = k; s !== kb; s += east ? 1 : -1) {
      const seam = (east ? s + 1 : s) * MAP_W;
      const t = (seam - xa) / (xb - xa);
      const lat = lonLat(0, ya + (yb - ya) * t)[1];
      parts[parts.length - 1].push(pt(east ? 180 : -180, lat));
      parts.push([pt(east ? -180 : 180, lat)]);
    }
    k = kb;
    const [lon, lat] = lonLat(xb, yb);
    parts[parts.length - 1].push(pt(lon, lat));
  }
  const lines = parts.filter((p) => p.length >= 2).map((p) => `(${p.join(', ')})`);
  if (lines.length === 0) return '';
  return lines.length === 1 ? `LINESTRING${lines[0]}` : `MULTILINESTRING(${lines.join(', ')})`;
}

/** Approximate length in km: Mercator segment lengths scaled by the local km per map unit. */
export function riverLengthKm(points: ArrayLike<number>): number {
  let km = 0;
  for (let i = 1; i < points.length / 3; i++) {
    const a = (i - 1) * 3, b = i * 3;
    km += Math.hypot(points[b] - points[a], points[b + 1] - points[a + 1]) * kmPerMu((points[a + 1] + points[b + 1]) / 2);
  }
  return km;
}

/** CSV field; WKT always contains commas, so quote whenever a separator, quote or newline appears. */
const csv = (s: string): string => (/[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s);

const attrs = (o: Record<string, string | number | undefined>): string =>
  Object.entries(o).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => `${k}=${v}`).join('; ');

export interface WktRow { wkt: string; layer: string; kind: string; name: string; id: string; attrs: string; }

/** The document's exportable geometry, one row per feature. */
export function wktRows(doc: Pick<MapDocument, 'rivers' | 'features'>): WktRow[] {
  const rows: WktRow[] = [];
  const named = new Map<string, FeatureEntity>();
  for (const f of doc.features.all()) if (f.riverId) named.set(f.riverId, f);
  for (const r of doc.rivers.all() as Iterable<RiverEntity>) {
    const wkt = riverToWkt(r.points);
    if (!wkt) continue;
    const f = named.get(r.id);
    rows.push({
      wkt, layer: 'rivers', kind: 'river', name: r.name || f?.name || '', id: r.id,
      attrs: attrs({ length_km: Math.round(riverLengthKm(r.points)), source: f && !isGeneratedFeatureId(f.id) ? 'drawn' : 'generated' }),
    });
  }
  for (const f of doc.features.all()) {
    if (f.kind === 'river') continue;
    const [lon, lat] = lonLat(f.x, f.y);
    const wkt = `POINT(${pt(lon, lat)})`;
    if (f.kind === 'deposit') {
      rows.push({ wkt, layer: 'deposits', kind: f.deposit?.type ?? 'deposit', name: f.name, id: f.id, attrs: attrs({ richness: f.deposit ? f.deposit.richness.toFixed(2) : undefined }) });
    } else if (AREA_KINDS.has(f.kind)) {
      rows.push({
        wkt, layer: 'areas', kind: f.kind, name: f.name, id: f.id,
        attrs: attrs({ tiles: f.tiles?.length, extent_km: f.extent > 0 ? Math.round(f.extent * kmPerMu(f.y)) : undefined, anchor: 'label' }),
      });
    } else {
      rows.push({ wkt, layer: 'settlements', kind: f.kind, name: f.name, id: f.id, attrs: attrs({ weight: f.weight }) });
    }
  }
  return rows;
}

/** The whole export as CSV text (header + one row per feature). */
export function exportWkt(doc: Pick<MapDocument, 'rivers' | 'features'>): string {
  const lines = ['wkt,layer,kind,name,id,attrs'];
  for (const r of wktRows(doc)) lines.push([r.wkt, r.layer, r.kind, r.name, r.id, r.attrs].map(csv).join(','));
  return lines.join('\n') + '\n';
}
