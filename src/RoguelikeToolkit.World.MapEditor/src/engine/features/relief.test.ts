import { describe, expect, it } from 'vitest';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { PlacementRule } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import { MAP_W } from '../core/geo';
import { FootprintMap, placeRelief } from './relief';
import { populate } from './populate';

/** A ridge running east-west along y = 400: height 1.0 on the spine, falling to 0.3 at 60 mu off it. */
const ridgeDoc = {
  elevationAt: (_x: number, y: number) => Math.max(-0.5, 1.0 - Math.abs(y - 400) / 60 * 0.7),
  biomeAt: () => 4,
  features: { all: () => [] as never[] },
} as unknown as MapDocument;

const SIZES: Record<string, { w: number; h: number; category: string }> = {
  'core.mountain': { w: 20, h: 18, category: 'mountain' },
  'core.mountainSnow': { w: 20, h: 18, category: 'mountain' },
  'core.hill': { w: 10, h: 5, category: 'hill' },
  'core.conifer': { w: 3, h: 6, category: 'tree' },
};
const lib = {
  variants: (a: string) => (a in SIZES ? 4 : 0),
  get: (a: string, v: number) => (a in SIZES ? { asset: a, variant: v, ax: 0.5, ay: 0.95, ...SIZES[a] } : undefined),
} as unknown as AssetLibrary;

const mountains: PlacementRule = {
  id: 'mountains', assets: ['core.mountain'], tiers: [{ above: 0.8, assets: ['core.mountainSnow'] }],
  layout: 'relief', ridge: 0.6, elevation: [0.47, 9], spacing: 8.5, density: 1, scale: [0.55, 1.5],
};
const hills: PlacementRule = { id: 'hills', assets: ['core.hill'], layout: 'relief', ridge: 0.25, clump: 0.55, elevation: [0.3, 0.47], spacing: 9, density: 0.85 };
/** The placer's body for a stamp: visual center (anchor raised by (ay - 0.5) of the height) and size. */
const body = (s: { asset: string; x: number; y: number; scale: number }) => {
  const z = SIZES[s.asset]!, w = z.w * s.scale, h = z.h * s.scale;
  return { x: s.x, y: s.y - h * 0.45, w, h };
};
const clear = (a: ReturnType<typeof body>, b: ReturnType<typeof body>) => {
  let dx = a.x - b.x; dx -= Math.round(dx / MAP_W) * MAP_W;
  return (dx / ((a.w + b.w) * 0.5 * 0.78)) ** 2 + ((a.y - b.y) / ((a.h + b.h) * 0.5 * 0.42)) ** 2 >= 0.999;
};
const place = (rule: PlacementRule, occ = new FootprintMap()) => placeRelief(ridgeDoc, rule, 1337, lib, 1, occ);

describe('placeRelief', () => {
  it('is deterministic', () => {
    expect(JSON.stringify(place(mountains))).toBe(JSON.stringify(place(mountains)));
  });

  it('puts the biggest peaks on the spine and smaller ones on the flanks', () => {
    const st = place(mountains);
    expect(st.length).toBeGreaterThan(50);
    const near = st.filter((s) => Math.abs(s.y - 400) < 10), far = st.filter((s) => Math.abs(s.y - 400) > 25);
    const mean = (a: typeof st) => a.reduce((t, s) => t + s.scale, 0) / a.length;
    expect(mean(near)).toBeGreaterThan(mean(far) + 0.2);
  });

  it('is far sparser than one stamp per spacing cell (no carpet)', () => {
    const band = (2 * 60 * (1 - 0.47) / 0.7) * MAP_W;   // area where the height is in the window
    const cells = band / (8.5 * 8.5);
    expect(place(mountains).length).toBeLessThan(cells * 0.6);   // the grid layout filled ~0.92 of them
  });

  it('keeps an elliptical gap between any two placed bodies', () => {
    const st = place(mountains);
    for (let i = 0; i < st.length; i++) for (let j = i + 1; j < st.length; j++) expect(clear(body(st[i]!), body(st[j]!))).toBe(true);
  });

  it('switches to the snow tier above its height', () => {
    for (const s of place(mountains)) {
      expect(s.asset).toBe(ridgeDoc.elevationAt(s.x, s.y) >= 0.8 ? 'core.mountainSnow' : 'core.mountain');
    }
  });

  it('shares footprints across rules: hills keep the gap from mountains too', () => {
    const occ = new FootprintMap();
    const m = place(mountains, occ), h = place(hills, occ);
    expect(h.length).toBeGreaterThan(0);
    for (const a of h) for (const b of m) expect(clear(body(a), body(b))).toBe(true);
  });
});

describe('populate with relief rules', () => {
  it('keeps scattered trees off hill and mountain bodies', () => {
    const forest: PlacementRule = { id: 'forest', assets: ['core.conifer'], elevation: [0, 99], spacing: 4, density: 1 };
    const st = populate(ridgeDoc, { seed: 7, rules: [mountains, forest], library: lib, featureAssets: () => [] });
    const occ = new FootprintMap();
    for (const s of st.filter((s) => s.asset.startsWith('core.mountain'))) occ.add(body(s));
    const trees = st.filter((s) => s.asset === 'core.conifer');
    expect(trees.length).toBeGreaterThan(0);
    for (const t of trees) expect(occ.covers(t.x, t.y)).toBe(false);
  });
});
