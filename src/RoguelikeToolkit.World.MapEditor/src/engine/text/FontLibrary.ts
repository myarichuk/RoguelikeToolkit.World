import opentype from 'opentype.js';

/**
 * Loads fonts as outline sources (opentype.js), keyed like content packs key them ("serif",
 * "italic", "caps"). Text is never rendered with the browser's font stack: labels are glyph
 * outlines, so the screen, the SVG export and any other renderer draw identical shapes.
 */
export class FontLibrary {
  private readonly fonts = new Map<string, opentype.Font>();
  private readonly loading = new Map<string, Promise<opentype.Font>>();
  version = 0;

  async load(key: string, url: string): Promise<opentype.Font> {
    const id = `${key}@${url}`;
    let p = this.loading.get(id);
    if (!p) {
      p = fetch(url).then(async (r) => {
        if (!r.ok) throw new Error(`font ${url}: HTTP ${r.status}`);
        return opentype.parse(await r.arrayBuffer());
      });
      this.loading.set(id, p);
    }
    const font = await p;
    if (this.fonts.get(key) !== font) { this.fonts.set(key, font); this.version++; }
    return font;
  }

  async loadAll(map: Record<string, string>): Promise<void> {
    await Promise.all(Object.entries(map).map(([k, u]) => this.load(k, u)));
  }

  /** Registers an already parsed font (tests, embedded fonts). */
  put(key: string, font: opentype.Font): void { this.fonts.set(key, font); this.version++; }

  get(key: string): opentype.Font | undefined {
    return this.fonts.get(key) ?? this.fonts.get('serif') ?? this.fonts.values().next().value;
  }
}
