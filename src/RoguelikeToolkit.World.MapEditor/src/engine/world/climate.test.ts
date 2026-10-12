import { describe, expect, it } from 'vitest';
import { MapDocument } from '../doc/MapDocument';
import { decodeDocument, encodeDocument } from '../doc/serialize';
import {
  CI, CLIMATE_KINDS, CLIMATE_STRIDE, describeClimate, ENGINE_CLIMATE_STRIDE, flowLabel, LEGACY_CLIMATE_STRIDE, paintedKelvin, projectClimate, rampColor, widenClimate,
} from './climate';

/** A raw engine record: temperature, rain, wind xyz, seasonal range, rain seasonality, ocean heat, current xyz, rain phase. */
const rec = (o: Partial<Record<'t' | 'p' | 'w' | 's' | 'r' | 'h' | 'c' | 'q', number | number[]>>): number[] => {
  const w = (o.w as number[]) ?? [0, 0, 0], c = (o.c as number[]) ?? [0, 0, 0];
  return [(o.t as number) ?? 0.5, (o.p as number) ?? 0.5, ...w, (o.s as number) ?? 0, (o.r as number) ?? 0, (o.h as number) ?? 0, ...c, (o.q as number) ?? 0];
};

describe('projectClimate', () => {
  // On the equator at longitude 0 the tile is (1, 0, 0): east is +y, north is +z.
  it('projects 3D wind and current onto east and north', () => {
    const raw = [...rec({ w: [0, 0.3, 0.4], c: [0, -0.2, 0] })];
    const out = projectClimate(raw, [1, 0, 0], 1);
    expect(out[CI.windE]).toBeCloseTo(0.3, 6);
    expect(out[CI.windN]).toBeCloseTo(0.4, 6);
    expect(out[CI.currentE]).toBeCloseTo(-0.2, 6);
    expect(out[CI.currentN]).toBeCloseTo(0, 6);
  });

  it('follows the tile around the globe: at longitude 90 east is -x', () => {
    const out = projectClimate(rec({ w: [-0.5, 0, 0] }), [0, 1, 0], 1);
    expect(out[CI.windE]).toBeCloseTo(0.5, 6);
    expect(out[CI.windN]).toBeCloseTo(0, 6);
  });

  it('north at a high latitude tilts toward the pole, and a tangent wind keeps its speed', () => {
    const lat = Math.PI / 3, p = [Math.cos(lat), 0, Math.sin(lat)];
    // A wind blowing straight north along the surface at this tile.
    const north = [-Math.sin(lat), 0, Math.cos(lat)];
    const out = projectClimate(rec({ w: north.map((v) => v * 0.6) }), p, 1);
    expect(out[CI.windN]).toBeCloseTo(0.6, 6);
    expect(out[CI.windE]).toBeCloseTo(0, 6);
  });

  it('copies the scalar fields and leaves the poles with no direction rather than a wrong one', () => {
    const out = projectClimate(rec({ t: 0.7, p: 0.2, s: 0.3, r: 0.4, h: -0.05, q: -0.4, w: [0.5, 0.5, 0] }), [0, 0, 1], 1);
    expect([out[CI.temperature], out[CI.rainfall], out[CI.seasonalRange], out[CI.rainSeasonality], out[CI.oceanHeat], out[CI.rainPhase]].map((v) => +v.toFixed(3))).toEqual([0.7, 0.2, 0.3, 0.4, -0.05, -0.4]);
    expect(out[CI.windE]).toBe(0);
    expect(out[CI.windN]).toBe(0);
    expect(out.length).toBe(CLIMATE_STRIDE);
  });

  it('strides match: twelve in, ten out, per tile', () => {
    const raw = [...rec({ t: 0.1 }), ...rec({ t: 0.9 })];
    expect(raw.length).toBe(2 * ENGINE_CLIMATE_STRIDE);
    const out = projectClimate(raw, [1, 0, 0, 0, 1, 0], 2);
    expect(out.length).toBe(2 * CLIMATE_STRIDE);
    expect(out[CLIMATE_STRIDE]).toBeCloseTo(0.9, 6);
  });
});

describe('ramps and readouts', () => {
  it('ends of a ramp are its first and last stops, and out-of-range values clamp', () => {
    for (const k of CLIMATE_KINDS) {
      expect(rampColor(k, k.lo - 5)).toEqual([k.stops[0][1], k.stops[0][2], k.stops[0][3]]);
      const last = k.stops[k.stops.length - 1];
      expect(rampColor(k, k.hi + 5)).toEqual([last[1], last[2], last[3]]);
    }
  });

  it('interpolates between stops', () => {
    const rain = CLIMATE_KINDS.find((k) => k.key === 'rainfall')!;
    const c = rampColor(rain, 0.175); // halfway between the first two stops
    expect(c[0]).toBe(Math.round((253 + 161) / 2));
  });

  it('names a direction as where the air is going, clockwise from north', () => {
    expect(flowLabel(0, 1)).toBe('N 1.00');
    expect(flowLabel(0.5, 0.5)).toMatch(/^NE /);
    expect(flowLabel(-1, 0)).toBe('W 1.00');
    expect(flowLabel(0.001, 0)).toBe('');
  });

  it('paint is shown in kelvin at the brush scale (full strength about 22 K)', () => {
    expect(paintedKelvin(127)).toBeCloseTo(22, 5);
    expect(paintedKelvin(-127)).toBeCloseTo(-22, 5);
  });

  it('describes a tile, and nothing without a climate', () => {
    const t = projectClimate([...rec({ t: 0.5, p: 0.6, w: [0, 0.5, 0], s: 0.2, h: 0.08, c: [0, 0.3, 0] })], [1, 0, 0], 1);
    const s = describeClimate(t, 0, 6.4, 0);
    expect(s).toContain('temp 0.50');
    expect(s).toContain('rain 0.60');
    expect(s).toContain('wind E 0.50');
    expect(s).toContain('current E 0.30 (warm)');
    expect(s).toContain('painted +6 K');
    expect(s).not.toContain('dry season');
    const wet = (r: number) => describeClimate(projectClimate([...rec({ r })], [1, 0, 0], 1), 0);
    expect(wet(0.8)).toContain('wet/dry seasons 0.80');
    expect(wet(0.4)).toContain('some dry season 0.40');
    expect(s).not.toContain('sea ice');
    expect(describeClimate(t, 0, 0, 0, 1)).toContain('sea ice in winter');
    expect(describeClimate(t, 0, 0, 0, 2)).toContain('sea ice all year');
    expect(describeClimate(undefined, 0, 0, 0, 2)).toBe('');
    expect(describeClimate(undefined, 0)).toBe('');
    expect(describeClimate(t, 5)).toBe('');
  });
});

describe('the climate model in a .wmap', () => {
  it('round-trips, and a file from before it existed loads as the calibrated defaults', () => {
    const doc = MapDocument.empty();
    doc.setSettings({ climateRefresh: false, meridionalWind: 0.1, windCellBlend: 0.5, oceanHeat: 0, oceanRain: 3, aeolian: 7 });
    const s = decodeDocument(encodeDocument(doc)).settings;
    expect([s.climateRefresh, s.meridionalWind, s.windCellBlend, s.oceanHeat, s.oceanRain, s.aeolian]).toEqual([false, 0.1, 0.5, 0, 3, 7]);
    const old = MapDocument.empty();
    for (const k of ['climateRefresh', 'meridionalWind', 'windCellBlend', 'oceanHeat', 'oceanRain', 'aeolian']) delete (old.settings as unknown as Record<string, unknown>)[k];
    const d = decodeDocument(encodeDocument(old)).settings;
    expect([d.climateRefresh, d.meridionalWind, d.windCellBlend, d.oceanHeat, d.oceanRain, d.aeolian]).toEqual([true, 0.3, 0, 1, 2, 4]);
  });
});

describe('the climate cache in a .wmap', () => {
  const make = (tiles: number, climate: Float32Array | undefined) => {
    const doc = MapDocument.empty();
    doc.world.tileXY = new Float32Array(tiles * 2);
    doc.physics = { edits: new Map(), baseline: null, generated: new Set(), tombstones: new Map(), climate };
    return doc;
  };
  it('round-trips, and is dropped when it does not fit the world or is absent', () => {
    const c = Float32Array.from({ length: 3 * CLIMATE_STRIDE }, (_, i) => i / 10);
    expect([...decodeDocument(encodeDocument(make(3, c))).physics!.climate!]).toEqual([...c]);
    expect(decodeDocument(encodeDocument(make(3, undefined))).physics!.climate).toBeUndefined();
    expect(decodeDocument(encodeDocument(make(3, new Float32Array(10)))).physics!.climate).toBeUndefined();
  });
});

describe('the rain phase', () => {
  const at = (r: number, q: number) => {
    const c = new Float32Array(CLIMATE_STRIDE);
    c[CI.rainSeasonality] = r; c[CI.rainPhase] = q;
    return describeClimate(c, 0, 0, 0);
  };
  it('says which season the rain comes in, and only when it is seasonal enough to matter', () => {
    expect(at(0.8, 0.8)).toContain('wet/dry seasons 0.80, rain in winter');
    expect(at(0.8, -0.8)).toContain('wet/dry seasons 0.80, rain in summer');
    expect(at(0.4, 0.4)).toContain('some dry season 0.40, rain in winter');
    // A phase this weak is an even year; an unknown one (an old cache) names no season at all.
    expect(at(0.5, 0.2)).toContain('some dry season 0.50');
    expect(at(0.5, 0.2)).not.toContain('rain in');
    expect(at(0.1, 0.9)).not.toContain('dry season');
  });

  it('is the signed layer: brown for summer rain, white for even, teal for winter rain', () => {
    const k = CLIMATE_KINDS.find((d) => d.key === 'rainSeason')!;
    expect(k.index).toBe(CI.rainPhase);
    const [s, e, w] = [rampColor(k, -1), rampColor(k, 0), rampColor(k, 1)];
    expect(s[0]).toBeGreaterThan(s[2]);   // red over blue: the warm end
    expect(e).toEqual([245, 245, 245]);
    expect(w[2]).toBeGreaterThan(w[0]);   // blue over red: the cool end
  });
});

describe('an older climate cache in a .wmap', () => {
  it('is widened with an unknown phase instead of being thrown away', () => {
    const old = Float32Array.from({ length: 3 * LEGACY_CLIMATE_STRIDE }, (_, i) => i + 1);
    const wide = widenClimate(old, 3)!;
    expect(wide.length).toBe(3 * CLIMATE_STRIDE);
    for (let t = 0; t < 3; t++) {
      for (let k = 0; k < LEGACY_CLIMATE_STRIDE; k++) expect(wide[t * CLIMATE_STRIDE + k]).toBe(old[t * LEGACY_CLIMATE_STRIDE + k]);
      expect(wide[t * CLIMATE_STRIDE + CI.rainPhase]).toBe(0);
    }
    const current = new Float32Array(3 * CLIMATE_STRIDE);
    expect(widenClimate(current, 3)).toBe(current);
    expect(widenClimate(new Float32Array(7), 3)).toBeUndefined();
  });
  it('survives a save and load through the document', () => {
    const doc = MapDocument.empty();
    doc.world.tileXY = new Float32Array(3 * 2);
    const old = Float32Array.from({ length: 3 * LEGACY_CLIMATE_STRIDE }, (_, i) => i / 10);
    doc.physics = { edits: new Map(), baseline: null, generated: new Set(), tombstones: new Map(), climate: old };
    const back = decodeDocument(encodeDocument(doc)).physics!.climate!;
    expect(back.length).toBe(3 * CLIMATE_STRIDE);
    expect(back[CLIMATE_STRIDE + CI.temperature]).toBeCloseTo(old[LEGACY_CLIMATE_STRIDE], 6);
  });
});
