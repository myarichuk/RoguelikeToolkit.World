import { describe, expect, it } from 'vitest';
import { BIOME_HINTS, THEME_HINTS } from '../ui/glossary';
import { MAP_COLORS, MAP_NUMBERS } from './ThemeEditorViewModel';

// The theme panel looks hints up by MapStyle key; a new colour or number without an entry would silently get no tooltip.
describe('theme editor tooltips', () => {
  it('has a plain-language hint for every map colour and number', () => {
    for (const c of [...MAP_COLORS, ...MAP_NUMBERS]) expect(THEME_HINTS[c.key], c.key).toMatch(/\S{3,}/);
  });
});

// The two biomes that are grown by the season of the rain say so, and point at the layer that shows why.
describe('biome swatch tooltips', () => {
  it('explain savanna and Mediterranean, and name the season each one wants', () => {
    expect(BIOME_HINTS.Savanna).toMatch(/summer/);
    expect(BIOME_HINTS.Mediterranean).toMatch(/winter/);
    expect(BIOME_HINTS.Mediterranean).toMatch(/dry summer/);
    for (const h of Object.values(BIOME_HINTS)) expect(h).toMatch(/Wet season/);
  });
});
