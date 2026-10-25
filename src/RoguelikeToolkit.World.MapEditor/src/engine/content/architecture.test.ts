import { describe, expect, it } from 'vitest';
import corePack from './core-pack.json';
import { PackRegistry } from './PackRegistry';
import { validatePack } from './validate';
import { hashString } from '../core/rng';
import { GENERATORS, UNITS_PER_MAP_UNIT, makeStamp } from '../procgen/stamps';
import type { ContentPack } from './types';

const extra = (architecture: unknown, assets: unknown[] = []) => ({ id: 'x', name: 'X', version: '1', assets, architecture });

describe('architecture pack section', () => {
  it('accepts the shipped pack', () => {
    const { pack, errors } = validatePack(corePack);
    expect(errors).toEqual([]);
    expect(pack?.architecture?.dwarven?.village).toEqual(['core.village.dwarven']);
    expect(pack?.featureAssets?.fort).toEqual(['core.fort']);
  });

  it('rejects an entry naming an unknown asset, and malformed entries', () => {
    expect(validatePack(extra({ dwarven: { village: ['core.nope'] } })).errors).toEqual(['architecture.dwarven.village[0]: unknown asset "core.nope"']);
    expect(validatePack(extra({ dwarven: { village: 'core.village' } })).errors).toEqual(['architecture.dwarven.village: expected an array']);
    expect(validatePack(extra({ dwarven: 3 })).errors).toEqual(['architecture.dwarven: expected an object']);
  });

  it('accepts assets of its own and of packs loaded before it', () => {
    expect(validatePack(extra({ dwarven: { village: ['x.v'] } }, [{ id: 'x.v', name: 'V', category: 'settlement', size: 7, generator: 'village' }])).errors).toEqual([]);
    expect(validatePack(extra({ dwarven: { village: ['core.village'] } }), new Set(['core.village'])).errors).toEqual([]);
    const reg = new PackRegistry();
    reg.add(corePack, 'http://x/');
    expect(() => reg.add(extra({ dwarven: { town: ['core.town'] } }), 'http://y/')).not.toThrow();
  });

  it('picks the style\'s asset, falls back to the kind\'s plain asset, and lets later packs override', () => {
    const reg = new PackRegistry();
    reg.add(corePack, 'http://x/');
    expect(reg.featureAssets('village', 'dwarven')).toEqual(['core.village.dwarven']);
    expect(reg.featureAssets('fort', 'stilt')).toEqual(['core.fort.stilt']);
    expect(reg.featureAssets('village')).toEqual(['core.village']);
    expect(reg.featureAssets('village', 'unheard_of')).toEqual(['core.village']);
    expect(reg.featureAssets('shrine', 'dwarven')).toEqual(['core.temple']);
    expect(reg.architectures()).toContain('sylvan');
    reg.add(extra({ dwarven: { village: ['core.town'] } }), 'http://y/');
    expect(reg.featureAssets('village', 'dwarven')).toEqual(['core.town']);
    expect(reg.featureAssets('town', 'dwarven')).toEqual(['core.town.dwarven']);
  });

  it('every shipped asset still fits the one stamp atlas', () => {
    // Mirrors AssetLibrary.build's sizing and shelf packing (4096 px square, 3 px padding).
    const items: { pw: number; ph: number }[] = [];
    for (const def of (corePack as unknown as ContentPack).assets ?? []) {
      if (!def.generator) continue;
      for (let v = 0; v < Math.max(1, def.variants ?? 1); v++) {
        const s = makeStamp(def.generator, (hashString(def.id) + v * 7919) >>> 0, def.params);
        const hMu = (s.h * def.size) / (GENERATORS[def.generator]!.defaultSize * UNITS_PER_MAP_UNIT);
        const px = Math.min(384, Math.max(96, hMu * 12)) / s.h;
        items.push({ pw: Math.ceil(s.w * px), ph: Math.ceil(s.h * px) });
      }
    }
    items.sort((a, b) => b.ph - a.ph);
    let x = 3, y = 3, shelf = 0;
    for (const it of items) { if (x + it.pw + 3 > 4096) { x = 3; y += shelf + 3; shelf = 0; } x += it.pw + 3; shelf = Math.max(shelf, it.ph); }
    expect(y + shelf + 3).toBeLessThanOrEqual(4096);
  });
});
