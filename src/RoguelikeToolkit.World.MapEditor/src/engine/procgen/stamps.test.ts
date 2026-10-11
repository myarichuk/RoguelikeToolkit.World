import { describe, it, expect } from 'vitest';
import { GENERATORS, DEFAULT_PALETTE, makeStamp, stampToSvg, stampScale } from './stamps';
import type { PaletteToken, Stamp } from './stamps';
import { Rng, mixSeed, pathD } from './ink';

const TOKENS = Object.keys(DEFAULT_PALETTE) as PaletteToken[];
const IDS = Object.keys(GENERATORS);

/** Absolute points (endpoints and control points) of a path written by pathD. */
function pathPoints(d: string): Array<[number, number]> {
  const toks = d.match(/[MlqtzLQTZ]|-?(?:\d+\.?\d*|\.\d+)/g) ?? [];
  const pts: Array<[number, number]> = [];
  let cmd = '', cur: [number, number] = [0, 0], start: [number, number] = [0, 0];
  let i = 0;
  const num = (): number => Number(toks[i++]);
  while (i < toks.length) {
    const t = toks[i]!;
    if (/[A-Za-z]/.test(t)) {
      cmd = t; i++;
      if (cmd === 'z' || cmd === 'Z') { cur = start; continue; }
    }
    if (cmd === 'M') { cur = [num(), num()]; start = cur; pts.push(cur); }
    else if (cmd === 'l' || cmd === 't') { cur = [cur[0] + num(), cur[1] + num()]; pts.push(cur); }
    else if (cmd === 'q') {
      const c: [number, number] = [cur[0] + num(), cur[1] + num()];
      pts.push(c);
      cur = [cur[0] + num(), cur[1] + num()]; pts.push(cur);
    } else throw new Error(`unexpected command ${cmd} in ${d.slice(0, 40)}`);
  }
  return pts;
}

describe('generator registry', () => {
  it('has every required generator id', () => {
    for (const id of ['mountain', 'mountainSnow', 'volcano', 'hill', 'conifer', 'deciduous', 'palm', 'deadTree', 'shrub', 'grassTuft', 'reeds', 'dunes', 'cactus', 'rock',
      'village', 'town', 'city', 'capital', 'castle', 'tower', 'ruins', 'temple', 'port', 'lighthouse', 'wave', 'ship', 'seaMonster', 'whirlpool', 'compass']) {
      expect(GENERATORS[id], id).toBeDefined();
      expect(GENERATORS[id]!.id).toBe(id);
      expect(GENERATORS[id]!.defaultSize).toBeGreaterThan(0);
    }
  });

  it('rejects unknown ids', () => {
    expect(() => makeStamp('nope', 1)).toThrow();
  });
});

describe.each(IDS)('%s', (id) => {
  const seeds = [0, 1, 42, 1234567, 0xffffffff];

  it('produces a well-formed stamp', () => {
    for (const seed of seeds) {
      const s = makeStamp(id, seed);
      expect(s.w).toBeGreaterThan(0);
      expect(s.h).toBeGreaterThan(0);
      expect(s.anchor[0]).toBeGreaterThanOrEqual(0);
      expect(s.anchor[0]).toBeLessThanOrEqual(s.w);
      expect(s.anchor[1]).toBeGreaterThanOrEqual(0);
      expect(s.anchor[1]).toBeLessThanOrEqual(s.h);
      expect(s.shapes.length).toBeGreaterThan(0);
      for (const sh of s.shapes) {
        expect(TOKENS).toContain(sh.fill);
        expect(TOKENS).toContain(sh.stroke);
        expect(sh.fill === 'none' && sh.stroke === 'none').toBe(false);
        expect(sh.d).toMatch(/^M[-\d. MlqtzLQTZ]*$/);
        expect(sh.d).not.toMatch(/\d\.\d\d/); // 1 decimal max
        expect(sh.d).not.toMatch(/NaN|Infinity/);
        if (sh.opacity !== undefined) { expect(sh.opacity).toBeGreaterThan(0); expect(sh.opacity).toBeLessThanOrEqual(1); }
      }
    }
  });

  it('keeps all geometry inside its box', () => {
    for (const seed of seeds) {
      const s = makeStamp(id, seed);
      for (const sh of s.shapes) for (const [x, y] of pathPoints(sh.d)) {
        expect(x).toBeGreaterThanOrEqual(-0.2);
        expect(y).toBeGreaterThanOrEqual(-0.2);
        expect(x).toBeLessThanOrEqual(s.w + 0.2);
        expect(y).toBeLessThanOrEqual(s.h + 0.2);
      }
    }
  });

  it('is deterministic and varies with the seed', () => {
    const a = JSON.stringify(makeStamp(id, 777));
    const b = JSON.stringify(makeStamp(id, 777));
    expect(a).toBe(b);
    const variants = new Set([1, 2, 3, 4, 5].map((s) => JSON.stringify(makeStamp(id, s))));
    expect(variants.size).toBe(5);
  });

  it('stays compact', () => {
    const limit = id === 'capital' ? 40000 : id === 'city' ? 26000 : id === 'compass' || id === 'castle' ? 14000 : 9000;
    for (const seed of seeds) expect(JSON.stringify(makeStamp(id, seed)).length).toBeLessThan(limit);
  });
});

describe('params', () => {
  it('clamps and applies params', () => {
    const one = makeStamp('mountain', 5, { peaks: 1 });
    const three = makeStamp('mountain', 5, { peaks: 3 });
    expect(three.shapes.length).toBeGreaterThan(one.shapes.length);
    expect(JSON.stringify(makeStamp('mountain', 5, { peaks: 99 }))).toBe(JSON.stringify(three));
  });

  it('mountainSnow has a snow cap, plain mountain does not', () => {
    expect(makeStamp('mountainSnow', 9).shapes.some((s) => s.fill === 'snow')).toBe(true);
    expect(makeStamp('mountain', 9).shapes.some((s) => s.fill === 'snow')).toBe(false);
    expect(makeStamp('mountain', 9, { snow: 1 }).shapes.some((s) => s.fill === 'snow')).toBe(true);
  });

  it('ignores non-finite params', () => {
    expect(JSON.stringify(makeStamp('hill', 3, { width: Number.NaN }))).toBe(JSON.stringify(makeStamp('hill', 3)));
  });
});

describe('determinism plumbing', () => {
  it('never uses Math.random', () => {
    const orig = Math.random;
    Math.random = () => { throw new Error('Math.random used'); };
    try { for (const id of IDS) makeStamp(id, 11); } finally { Math.random = orig; }
  });

  it('rng and seed hash are stable', () => {
    const r = new Rng(mixSeed('mountain', 1));
    const seq = [r.next(), r.next(), r.next()];
    const r2 = new Rng(mixSeed('mountain', 1));
    expect([r2.next(), r2.next(), r2.next()]).toEqual(seq);
    for (const v of seq) { expect(v).toBeGreaterThanOrEqual(0); expect(v).toBeLessThan(1); }
    expect(mixSeed('a', 1)).not.toBe(mixSeed('a', 2));
    expect(mixSeed('ab', 1)).not.toBe(mixSeed('a', 'b1'));
  });
});

describe('serialization', () => {
  it('writes smooth paths with one point per vertex that round-trip through the parser', () => {
    const d = pathD([{ pts: [[0, 0], [10, 0], [10, 10], [0, 10]], closed: true, smooth: true }]);
    expect(d).toBe('M0 5q0-5 5-5t5 5-5 5-5-5z');
    const pts = pathPoints(d);
    expect(pts[0]).toEqual([0, 5]);
    expect(pts[pts.length - 1]).toEqual([0, 5]);
  });

  it('stampToSvg emits one path per shape with palette colours', () => {
    const s: Stamp = makeStamp('conifer', 3);
    const svg = stampToSvg(s);
    expect((svg.match(/<path /g) ?? []).length).toBe(s.shapes.length);
    expect(svg).toContain(DEFAULT_PALETTE.ink);
    expect(svg).not.toContain('<svg');
    const custom = stampToSvg(s, { ...DEFAULT_PALETTE, ink: '#000000' });
    expect(custom).toContain('#000000');
  });

  it('stampScale maps box units to map units', () => {
    expect(stampScale('mountain')).toBeCloseTo(1 / 5);
    expect(stampScale('mountain', 36)).toBeCloseTo(2 / 5);
  });
});

describe('performance', () => {
  it('generates 300 stamps well under 100 ms', () => {
    const ids = IDS.filter((id) => id !== 'capital' && id !== 'city' && id !== 'compass');
    for (let i = 0; i < 60; i++) makeStamp(ids[i % ids.length]!, i); // warm up the JIT
    const t0 = performance.now();
    for (let i = 0; i < 300; i++) makeStamp(ids[i % ids.length]!, 1000 + i);
    const ms = performance.now() - t0;
    expect(ms).toBeLessThan(100);
  });
});
