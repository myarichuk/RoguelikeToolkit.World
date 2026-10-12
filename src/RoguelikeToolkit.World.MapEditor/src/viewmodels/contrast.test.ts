import { describe, expect, it } from 'vitest';
import { chromeContrastIssues, contrastRatio } from './contrast';

describe('contrast', () => {
  it('matches the WCAG reference values', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#fff', '#fff')).toBeCloseTo(1, 5);
    expect(contrastRatio('#777777', '#ffffff')).toBeCloseTo(4.48, 1);
  });
  it('is unknown for non-hex colors', () => {
    expect(contrastRatio('rgb(0,0,0)', '#fff')).toBeNull();
  });
  it('flags only the failing pairs', () => {
    const ok = { '--fg': '#e8e0d0', '--bg': '#1d1a17', '--panel': '#26221d', '--muted': '#a59a88', '--accent': '#c8a46a' };
    expect(chromeContrastIssues(ok)).toEqual([]);
    const bad = chromeContrastIssues({ ...ok, '--fg': '#2a2622' });
    expect(bad.map((i) => `${i.fg}/${i.bg}`)).toEqual(['--fg/--bg', '--fg/--panel']);
  });
});
