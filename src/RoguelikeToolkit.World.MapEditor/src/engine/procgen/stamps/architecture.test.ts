import { describe, expect, it } from 'vitest';
import { hashString } from '../../core/rng';
import { GENERATORS, makeStamp } from './index';
import corePack from '../../content/core-pack.json';
import type { ContentPack } from '../../content/types';

const pack = corePack as unknown as ContentPack;
const assets = new Map((pack.assets ?? []).map((a) => [a.id, a]));
const STYLED_KINDS = ['capital', 'city', 'town', 'village', 'port', 'fort', 'ruin'];

/** Fingerprints of the classic look, taken before the style params existed: the defaults must keep drawing it. */
const CLASSIC: Record<string, number> = {
  'village#1': 2633474354, 'village#42': 3985373262, 'town#1': 2509773572, 'town#42': 4093167987, 'city#1': 840735898, 'city#42': 740824281,
  'capital#1': 1840485838, 'capital#42': 2557139798, 'castle#1': 2878053005, 'castle#42': 3470101681, 'ruins#1': 4064992355, 'ruins#42': 3820855513,
  'port#1': 2788927109, 'port#42': 1384604634,
};

describe('architectural style params', () => {
  it('draw the classic look by default, byte for byte', () => {
    for (const [key, hash] of Object.entries(CLASSIC)) {
      const [id, seed] = key.split('#');
      expect(hashString(JSON.stringify(makeStamp(id!, Number(seed)))), key).toBe(hash);
    }
  });

  it('spelling out every default changes nothing', () => {
    for (const id of ['village', 'town', 'city', 'capital', 'castle', 'fort', 'ruins', 'port']) {
      const spec = GENERATORS[id]!.params!;
      const defaults = Object.fromEntries(Object.entries(spec).map(([k, v]) => [k, v.default]));
      expect(JSON.stringify(makeStamp(id, 5, defaults)), id).toBe(JSON.stringify(makeStamp(id, 5)));
    }
  });

  it('cover every value of every enumerated param without breaking', () => {
    const enums: Record<string, number> = { house: 8, wall: 7, towerForm: 3, spire: 5, centre: 6, keep: 7 };
    for (const id of ['village', 'town', 'city', 'capital', 'castle', 'fort', 'port']) {
      for (const [k, max] of Object.entries(enums)) {
        if (!GENERATORS[id]!.params![k]) continue;
        for (let v = 0; v <= max; v++) {
          const s = makeStamp(id, 3, { [k]: v, wall: k === 'wall' ? v : 0 });
          expect(s.shapes.length, `${id} ${k}=${v}`).toBeGreaterThan(0);
          for (const sh of s.shapes) expect(sh.d, `${id} ${k}=${v}`).not.toMatch(/NaN|Infinity/);
        }
      }
    }
    for (let v = 0; v <= 8; v++) expect(makeStamp('ruins', 3, { ruin: v }).shapes.length).toBeGreaterThan(0);
    for (let v = 0; v <= 3; v++) expect(makeStamp('fort', 3, { layout: v }).shapes.length).toBeGreaterThan(0);
  });
});

describe('shipped architectural styles', () => {
  const styles = Object.entries(pack.architecture ?? {});

  it('ship twelve styles, each covering every styled kind', () => {
    expect(styles.map(([k]) => k).sort()).toEqual(['aerie', 'burrow', 'desert', 'drow', 'dwarven', 'elven_high', 'horde', 'human', 'longhouse', 'stilt', 'sylvan', 'tinker']);
    for (const [style, kinds] of styles) for (const kind of STYLED_KINDS) expect(kinds[kind]?.length, `${style}.${kind}`).toBeGreaterThan(0);
  });

  for (const [style, kinds] of styles) {
    it(`${style}: every kind generates deterministically${style === 'human' ? '' : ' and differs from the classic look'}`, () => {
      for (const kind of STYLED_KINDS) {
        const def = assets.get(kinds[kind]![0]!)!;
        expect(def, `${style}.${kind}`).toBeDefined();
        const gen = GENERATORS[def.generator!]!;
        for (const seed of [1, 99]) {
          const a = JSON.stringify(makeStamp(gen.id, seed, def.params));
          expect(JSON.stringify(makeStamp(gen.id, seed, def.params))).toBe(a);
          const s = JSON.parse(a) as ReturnType<typeof makeStamp>;
          expect(s.shapes.length).toBeGreaterThan(0);
          expect(a).not.toMatch(/NaN|Infinity/);
          if (style !== 'human') expect(a, `${style}.${kind}`).not.toBe(JSON.stringify(makeStamp(gen.id, seed)));
        }
      }
    });
  }

  it('give each style its own look (no two styles share a kind\'s params)', () => {
    for (const kind of STYLED_KINDS) {
      const seen = new Set<string>();
      for (const [, kinds] of styles) seen.add(JSON.stringify(assets.get(kinds[kind]![0]!)!.params ?? {}));
      expect(seen.size, kind).toBe(styles.length);
    }
  });
});
