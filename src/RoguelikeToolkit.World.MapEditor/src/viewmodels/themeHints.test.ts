import { describe, expect, it } from 'vitest';
import { THEME_HINTS } from '../ui/glossary';
import { MAP_COLORS, MAP_NUMBERS } from './ThemeEditorViewModel';

// The theme panel looks hints up by MapStyle key; a new colour or number without an entry would silently get no tooltip.
describe('theme editor tooltips', () => {
  it('has a plain-language hint for every map colour and number', () => {
    for (const c of [...MAP_COLORS, ...MAP_NUMBERS]) expect(THEME_HINTS[c.key], c.key).toMatch(/\S{3,}/);
  });
});
