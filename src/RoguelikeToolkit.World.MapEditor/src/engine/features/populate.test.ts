import { describe, expect, it } from 'vitest';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { PlacementRule } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import { placeVariant, populate, settlementAsset, tuneRules } from './populate';
import type { FeatureEntity } from '../doc/types';

const stubDoc = (biome: number) => ({
  elevationAt: () => 0.3,
  biomeAt: () => biome,
  features: { all: () => [] as never[] },
}) as unknown as MapDocument;
const stubLib = (count: number) => ({ variants: () => count, get: () => ({ w: 5, h: 6, ax: 0.5, ay: 1 }) }) as unknown as AssetLibrary;
const rule: PlacementRule = {
  id: 't', assets: ['core.conifer'], biomes: [3], elevation: [0, 99], spacing: 64, density: 1,
};
const run = (variants: number) =>
  populate(stubDoc(3), { seed: 1337, rules: [rule], library: stubLib(variants), featureAssets: () => [] });

describe('populate on a shore', () => {
  it('drops a land stamp whose base reaches into water, though its anchor is dry', () => {
    const shore = { ...stubDoc(3), elevationAt: (x: number) => (x < 1000 ? 0.3 : -0.2) } as unknown as MapDocument;
    const opts = { seed: 1337, rules: [rule], library: stubLib(2), featureAssets: () => [] };
    const dry = populate(shore, opts);
    expect(dry.length).toBeGreaterThan(0);
    expect(dry.every((st) => st.x < 1000 - 2)).toBe(true);
  });
});

describe('placeVariant', () => {
  it('bumps a variant that would repeat a neighbour', () => {
    const seen = new Map<number, string>([[10 * 32 + 9, 'core.conifer#2']]);
    expect(placeVariant(() => 2 / 1e6, 4, seen, 32, 10, 10, 'core.conifer')).toBe(3);
  });

  it('steps past several taken neighbours to the free variant', () => {
    const seen = new Map<number, string>([[10 * 32 + 9, 'core.conifer#0'], [9 * 32 + 10, 'core.conifer#1']]);
    expect(placeVariant(() => 0, 6, seen, 32, 10, 10, 'core.conifer')).toBe(2);
  });

  it('stands on a single variant instead of looping', () => {
    expect(placeVariant(() => 0.99, 1, new Map(), 32, 0, 0, 'core.rock')).toBe(0);
  });
});

describe('populate', () => {
  it('is deterministic: the same seed rebuilds the same stamps', () => {
    expect(JSON.stringify(run(4))).toBe(JSON.stringify(run(4)));
  });

  it('never repeats the exact asset and variant on neighbouring cells when a free variant exists', () => {
    // Six variants against four checked neighbours: a satisfying value always exists, and the
    // bump visits every residue, so it is always found. (With four or fewer variants the
    // neighbours can cover them all and no value satisfies; the placer keeps best effort.)
    const stamps = run(6);
    expect(stamps.length).toBeGreaterThan(100);
    const byCell = new Map(stamps.map((s) => [s.id, `${s.asset}#${s.variant}`]));
    for (const s of stamps) {
      const m = /_(\d+)_(\d+)$/.exec(s.id)!;
      const cx = Number(m[1]), cy = Number(m[2]);
      for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
        if (!ox && !oy) continue;
        const n = byCell.get(`st_t_${((cx + ox) % 32 + 32) % 32}_${cy + oy}`);
        if (n !== undefined) expect(n, `${s.id} vs neighbour`).not.toBe(`${s.asset}#${s.variant}`);
      }
    }
  });

  it('keeps scatter rotation a hand-placed lean', () => {
    for (const s of run(4)) expect(Math.abs(s.rotation)).toBeLessThanOrEqual(0.070001);
  });
});

describe('tuneRules', () => {
  const rules: PlacementRule[] = [
    { id: 'a', assets: ['x'], spacing: 5, density: 0.8, scale: [0.5, 1.5] },
    { id: 'b', assets: ['x'], spacing: 5, density: 0.4 },
  ];

  it('scales density and scale of tuned rules only', () => {
    const [a, b] = tuneRules(rules, { a: { density: 0.5, scale: 2 }, b: { scale: 3 } });
    expect(a.density).toBeCloseTo(0.4);
    expect(a.scale).toEqual([1, 3]);
    expect(b).toEqual(rules[1]);
    expect(rules[0].density).toBe(0.8);
  });

  it('passes rules through without tuning', () => {
    expect(tuneRules(rules, undefined)).toBe(rules);
  });
});

describe('settlementAsset', () => {
  const styled: Record<string, Record<string, string[]>> = { dwarven: { village: ['core.village.dwarven'], port: ['core.port.dwarven'], fort: ['core.fort.dwarven'], ruin: ['core.ruin.dwarven'] } };
  const plain: Record<string, string[]> = { village: ['core.village'], town: ['core.town'], port: ['core.port'], fort: ['core.fort'], ruin: ['core.ruins'], shrine: ['core.temple'] };
  // The registry's contract: the style's assets for the kind, else the kind's plain ones.
  const lookup = (kind: string, arch?: string) => (arch && styled[arch]?.[kind]) || plain[kind] || [];
  const feature = (kind: FeatureEntity['kind'], architecture?: string, coast = false): FeatureEntity => ({
    id: 'f_1', kind, name: '', x: 0, y: 0, angle: 0, extent: 0, weight: 1, culture: '', details: coast ? [['Water access', 'coast']] : [], ...(architecture ? { architecture } : {}),
  });

  it('draws a place in its realm\'s architecture', () => {
    expect(settlementAsset(feature('village', 'dwarven'), lookup)).toBe('core.village.dwarven');
    expect(settlementAsset(feature('fort', 'dwarven'), lookup)).toBe('core.fort.dwarven');
    expect(settlementAsset(feature('ruin', 'dwarven'), lookup)).toBe('core.ruin.dwarven');
  });

  it('makes a coastal town a port in its style', () => {
    expect(settlementAsset(feature('town', 'dwarven', true), lookup)).toBe('core.port.dwarven');
    expect(settlementAsset(feature('town', undefined, true), lookup)).toBe('core.port');
  });

  it('falls back to the plain asset for a kind or style without one, and for places without history', () => {
    expect(settlementAsset(feature('town', 'dwarven'), lookup)).toBe('core.town');
    expect(settlementAsset(feature('village', 'unknown'), lookup)).toBe('core.village');
    expect(settlementAsset(feature('village'), lookup)).toBe('core.village');
    expect(settlementAsset(feature('fort'), lookup)).toBe('core.fort');
    expect(settlementAsset(feature('river'), lookup)).toBeNull();
  });
});
