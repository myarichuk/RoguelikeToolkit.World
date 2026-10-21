import { describe, expect, it } from 'vitest';
import corePack from './core-pack.json';

/** sRGB hex -> CIE Lab (D65). ΔE76 is crude but monotone enough to catch "these two are the same colour". */
function lab(hex: string): [number, number, number] {
  const lin = [1, 3, 5].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  const [r, g, b] = lin;
  const f = (v: number) => (v > 0.008856 ? Math.cbrt(v) : 7.787 * v + 16 / 116);
  const x = f((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047);
  const y = f(0.2126 * r + 0.7152 * g + 0.0722 * b);
  const z = f((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883);
  return [116 * y - 16, 500 * (x - y), 200 * (y - z)];
}
const dE = (a: string, b: string): number => Math.hypot(...lab(a).map((v, i) => v - lab(b)[i]) as [number, number, number]);

function closest(colors: Record<string, string>, names: Map<string, string>): { d: number; pair: string } {
  // Ocean floor (0) is never shown on land, so it may match anything.
  const ids = Object.keys(colors).filter((k) => k !== '0');
  let best = { d: Infinity, pair: '' };
  for (let i = 0; i < ids.length; i++) for (let j = i + 1; j < ids.length; j++) {
    const d = dE(colors[ids[i]], colors[ids[j]]);
    if (d < best.d) best = { d, pair: `${names.get(ids[i])} / ${names.get(ids[j])}` };
  }
  return best;
}

// Plains drew in the same tan as desert (ΔE 5), so a plains world read as one desert. The floors sit a little under what
// the shipped palettes measure, so a retouch that makes any two biomes read as one colour fails here and names the pair.
describe('core pack biome colours stay distinguishable', () => {
  const names = new Map(corePack.biomes.map((b) => [String(b.id), b.name]));
  // Sepia ink is near-monochrome by design (biomes are a 15% tint over the paper; stamps carry the meaning), so its
  // floor only catches two biomes collapsing into the very same colour.
  const floors: Record<string, number> = { parchment: 10, atlas: 10, nocturne: 6, sepia: 1 };

  for (const theme of corePack.themes) {
    it(`theme ${theme.id}: every pair of land biomes differs by at least ${floors[theme.id] ?? 10} ΔE`, () => {
      const { d, pair } = closest(theme.biomes as Record<string, string>, names);
      expect(d, pair).toBeGreaterThanOrEqual(floors[theme.id] ?? 10);
    });
    it(`theme ${theme.id}: plains is clearly not desert, farmland or savanna`, () => {
      const b = theme.biomes as Record<string, string>;
      for (const other of ['2', '10', '11']) expect(dE(b['1'], b[other]), `plains / ${names.get(other)}`).toBeGreaterThan(floors[theme.id] ?? 10);
    });
  }

  it('the biome-view (vivid) palette keeps every pair apart too', () => {
    const vivid = Object.fromEntries(corePack.biomes.map((b) => [String(b.id), b.vivid]));
    const { d, pair } = closest(vivid, names);
    expect(d, pair).toBeGreaterThanOrEqual(7);
  });
});
