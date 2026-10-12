import { describe, expect, it } from 'vitest';
import { MAP_H, MAP_W } from '../core/geo';
import { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, RiverEntity } from '../doc/types';
import { exportWkt, riverToWkt, wktRows } from './WktExporter';

const feature = (id: string, kind: FeatureEntity['kind'], extra: Partial<FeatureEntity> = {}): FeatureEntity => ({
  id, kind, name: id, x: MAP_W / 2, y: MAP_H / 2, angle: 0, extent: 0, weight: 1, details: [], culture: '', ...extra,
});

describe('riverToWkt', () => {
  it('writes lon lat, centre of the map being 0 0', () => {
    expect(riverToWkt([MAP_W / 2, MAP_H / 2, 1, MAP_W / 2 + MAP_W / 36, MAP_H / 2, 1])).toBe('LINESTRING(0.000000 0.000000, 10.000000 0.000000)');
  });

  it('cuts at the antimeridian and interpolates the crossing, eastwards', () => {
    const w = riverToWkt([MAP_W - 10, 700, 1, MAP_W + 10, 800, 1]);
    expect(w).toMatch(/^MULTILINESTRING\(\(/);
    const parts = [...w.matchAll(/\(([^()]+)\)/g)].map((m) => m[1].split(', ').map((p) => p.split(' ').map(Number)));
    expect(parts).toHaveLength(2);
    expect(parts[0][parts[0].length - 1][0]).toBe(180);
    expect(parts[1][0][0]).toBe(-180);
    // The two halves meet at the same latitude, midway between the endpoints'.
    expect(parts[0][parts[0].length - 1][1]).toBe(parts[1][0][1]);
    const [a, b] = [parts[0][0][1], parts[1][parts[1].length - 1][1]];
    expect(parts[1][0][1]).toBeLessThan(a);
    expect(parts[1][0][1]).toBeGreaterThan(b);
  });

  it('cuts westwards as well, and a river wholly past the edge stays one line', () => {
    expect(riverToWkt([10, 700, 1, -10, 700, 1])).toMatch(/^MULTILINESTRING/);
    expect(riverToWkt([MAP_W + 10, 700, 1, MAP_W + 30, 700, 1])).toMatch(/^LINESTRING\(-/);
  });

  it('is empty for a degenerate river', () => { expect(riverToWkt([1, 1, 1])).toBe(''); });
});

describe('exportWkt', () => {
  it('exports edited rivers (drawn ones included), settlements, deposits and area anchors, and skips deleted rivers', () => {
    const doc = MapDocument.empty();
    const rv = (id: string, name: string): RiverEntity => ({ id, name, points: [100, 700, 2, 110, 710, 2, 130, 720, 2] });
    doc.rivers.add([rv('r1', 'Anduin'), rv('r2', 'Hand "Drawn", Brook')]);
    doc.features.add([
      feature('f_river:1', 'river', { riverId: 'r1' }),
      feature('f_river_x7k2', 'river', { riverId: 'r2' }),
      feature('f_settlement:1', 'capital', { name: 'Gondor' }),
      feature('f_deposit:1', 'deposit', { deposit: { type: 'gold', richness: 0.8 } }),
      feature('f_lake:1', 'lake', { tiles: [1, 2, 3], extent: 4 }),
    ]);
    const rows = wktRows(doc);
    expect(rows.map((r) => r.layer).sort()).toEqual(['areas', 'deposits', 'rivers', 'rivers', 'settlements']);
    expect(rows.find((r) => r.id === 'r1')!.attrs).toContain('source=generated');
    expect(rows.find((r) => r.id === 'r2')!.attrs).toContain('source=drawn');
    expect(rows.find((r) => r.kind === 'gold')!.attrs).toBe('richness=0.80');
    const csv = exportWkt(doc);
    expect(csv.split('\n')[0]).toBe('wkt,layer,kind,name,id,attrs');
    expect(csv).toContain('"Hand ""Drawn"", Brook"');
    doc.rivers.remove(['r2']);
    expect(wktRows(doc).filter((r) => r.layer === 'rivers').map((r) => r.id)).toEqual(['r1']);
  });
});
