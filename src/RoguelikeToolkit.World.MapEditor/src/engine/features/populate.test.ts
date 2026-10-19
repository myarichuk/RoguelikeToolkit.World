import { describe, expect, it } from 'vitest';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { PlacementRule } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import { placeVariant, populate } from './populate';

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
