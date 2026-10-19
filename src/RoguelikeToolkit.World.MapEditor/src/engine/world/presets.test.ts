import { describe, expect, it } from 'vitest';
import { CLIMATE_MODEL_DEFAULTS, CLIMATE_MODEL_LIMITS, EARTH, PLANET_LIMITS } from './protocol';
import { DETAIL_LEVELS, estimateGenerateMs, formatEstimate, matchPlanetPreset, modelFor, PLANET_PRESETS, TECTONIC_PRESETS, tilesOf } from './presets';

const within = (v: number, [lo, hi]: readonly [number, number]) => v >= lo && v <= hi;

describe('planet presets', () => {
  it('stay inside the limits the generator accepts, with unique ids', () => {
    expect(new Set(PLANET_PRESETS.map((p) => p.id)).size).toBe(PLANET_PRESETS.length);
    for (const p of PLANET_PRESETS) {
      expect(within(p.planet.obliquity, PLANET_LIMITS.obliquity), p.id).toBe(true);
      expect(within(p.planet.flux, PLANET_LIMITS.flux), p.id).toBe(true);
      expect(within(p.planet.rotation, PLANET_LIMITS.rotation), p.id).toBe(true);
      const m = modelFor(p);
      for (const k of ['meridionalWind', 'windCellBlend', 'oceanHeat', 'oceanRain', 'aeolian'] as const) expect(within(m[k], CLIMATE_MODEL_LIMITS[k]), `${p.id}.${k}`).toBe(true);
    }
  });

  it('starts from Earth and the calibrated model, and every preset says what it changes', () => {
    const earth = PLANET_PRESETS.find((p) => p.id === 'earthlike')!;
    expect(earth.planet).toEqual(EARTH);
    expect(modelFor(earth)).toEqual(CLIMATE_MODEL_DEFAULTS);
    for (const p of PLANET_PRESETS) expect(p.effects.length, p.id).toBeGreaterThan(0);
  });

  it('are recognised from their own settings, and custom settings match none', () => {
    for (const p of PLANET_PRESETS) expect(matchPlanetPreset(p.planet, modelFor(p))?.id).toBe(p.id);
    expect(matchPlanetPreset({ ...EARTH, obliquity: 31 }, CLIMATE_MODEL_DEFAULTS)).toBeNull();
  });
});

describe('tectonic presets and detail', () => {
  it('keep plates and wind erosion in range', () => {
    for (const t of TECTONIC_PRESETS) {
      expect(t.plates).toBeGreaterThanOrEqual(2);
      expect(t.plates).toBeLessThanOrEqual(40);
      expect(within(t.aeolian, CLIMATE_MODEL_LIMITS.aeolian), t.id).toBe(true);
    }
  });

  it('count tiles like the icosphere and scale the estimate with them', () => {
    expect(tilesOf(5)).toBe(10242);
    expect(DETAIL_LEVELS.map((d) => d.size)).toEqual([4, 5, 6, 7]);
    expect(estimateGenerateMs(6)).toBeGreaterThan(estimateGenerateMs(5) * 3.5);
    expect(estimateGenerateMs(5, 1)).toBe(10242);
  });

  it('words an estimate without false precision', () => {
    expect(formatEstimate(700)).toBe('about a second');
    expect(formatEstimate(4200)).toBe('about 4 s');
    expect(formatEstimate(150_000)).toBe('about 3 min');
  });
});
