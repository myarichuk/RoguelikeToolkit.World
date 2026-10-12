/** WCAG 2.x contrast for the editor-chrome colors, so the Theme panel can warn before the UI becomes unreadable. */

export const MIN_TEXT_CONTRAST = 4.5;

/** Parses #rgb / #rrggbb. Anything else (named colors, rgb(), color-mix) is unknown and not checked. */
export function parseHex(c: string): [number, number, number] | null {
  const m = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(c.trim());
  if (!m) return null;
  const h = m[1].length === 3 ? [...m[1]].map((x) => x + x).join('') : m[1];
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as [number, number, number];
}

function luminance([r, g, b]: [number, number, number]): number {
  const lin = (v: number) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

export function contrastRatio(a: string, b: string): number | null {
  const ra = parseHex(a), rb = parseHex(b);
  if (!ra || !rb) return null;
  const [hi, lo] = [luminance(ra), luminance(rb)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** [text token, background token] pairs the stylesheet actually draws together. */
export const CHROME_PAIRS: [string, string][] = [
  ['--fg', '--bg'], ['--fg', '--panel'], ['--muted', '--bg'], ['--muted', '--panel'], ['--bg', '--accent'],
];

export interface ContrastIssue { fg: string; bg: string; ratio: number }

export function chromeContrastIssues(ui: Record<string, string>): ContrastIssue[] {
  const out: ContrastIssue[] = [];
  for (const [fg, bg] of CHROME_PAIRS) {
    if (!ui[fg] || !ui[bg]) continue;
    const ratio = contrastRatio(ui[fg], ui[bg]);
    if (ratio !== null && ratio < MIN_TEXT_CONTRAST) out.push({ fg, bg, ratio });
  }
  return out;
}
