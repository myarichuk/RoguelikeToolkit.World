import { hashString } from '../core/rng';
import { GENERATORS, UNITS_PER_MAP_UNIT, makeStamp, stampToSvg, type Stamp } from '../procgen/stamps';
import type { PaletteToken } from '../procgen/ink';
import type { AssetDef, Theme } from './types';

/** One drawable variant of an asset: where it lives in the atlas and how it sits on the map. */
export interface AssetEntry {
  asset: string;
  variant: number;
  /** Atlas UV rect. */
  u0: number; v0: number; u1: number; v1: number;
  /** Size on the map at scale 1 (map units). */
  w: number; h: number;
  /** Anchor as a fraction of the box. */
  ax: number; ay: number;
  /** The asset's category ("mountain", "tree", "settlement"...): drives zoom level-of-detail. */
  category: string;
  /** Vector form for export: symbol content in its own viewBox. */
  viewBox: [number, number];
  svg: string;
}

const PAD = 3;
const ATLAS = 4096;

/**
 * Turns content-pack assets into GPU-ready sprites and export-ready symbols. Procedural assets
 * are generated from (generator, seed = hash(asset id) + variant); authored SVG assets are
 * rasterized through the browser. Colors are resolved against the active theme's palette, so
 * switching themes re-inks every stamp without touching the document.
 */
export class AssetLibrary {
  private entries = new Map<string, AssetEntry>();
  private byAsset = new Map<string, AssetEntry[]>();
  canvas: OffscreenCanvas | HTMLCanvasElement | null = null;
  version = 0;

  get(asset: string, variant: number): AssetEntry | undefined {
    const list = this.byAsset.get(asset);
    if (!list?.length) return undefined;
    return this.entries.get(`${asset}#${variant}`) ?? list[((variant % list.length) + list.length) % list.length];
  }

  variants(asset: string): number { return this.byAsset.get(asset)?.length ?? 0; }

  /** (Re)builds the atlas for a set of assets under a theme. */
  async build(assets: Map<string, AssetDef>, theme: Theme): Promise<void> {
    type Item = { def: AssetDef; variant: number; stamp?: Stamp; svgImage?: ImageBitmap; vb: [number, number]; ax: number; ay: number; px: number; pw: number; ph: number; svg: string };
    const items: Item[] = [];
    const palette = theme.palette;
    for (const def of assets.values()) {
      const count = Math.max(1, def.variants ?? 1);
      if (def.generator && GENERATORS[def.generator]) {
        const gen = GENERATORS[def.generator];
        for (let v = 0; v < count; v++) {
          const stamp = makeStamp(def.generator, (hashString(def.id) + v * 7919) >>> 0, def.params);
          const unitsTall = gen.defaultSize * UNITS_PER_MAP_UNIT;     // box units for the nominal height
          const muPerUnit = def.size / unitsTall;
          const hMu = stamp.h * muPerUnit;
          const px = Math.min(384, Math.max(96, hMu * 12)) / stamp.h;   // atlas px per box unit
          items.push({
            def, variant: v, stamp, vb: [stamp.w, stamp.h], ax: stamp.anchor[0] / stamp.w, ay: stamp.anchor[1] / stamp.h,
            px, pw: Math.ceil(stamp.w * px), ph: Math.ceil(stamp.h * px), svg: stampToSvg(stamp, palette),
          });
        }
      } else if (def.svg) {
        const { markup, vb } = resolveSvg(def.svg, palette);
        const ph = Math.min(384, Math.max(96, def.size * 12));
        const pw = Math.ceil(ph * vb[0] / vb[1]);
        const blob = new Blob([markup], { type: 'image/svg+xml' });
        const img = await createImageBitmap(await loadImage(URL.createObjectURL(blob)), { resizeWidth: pw, resizeHeight: ph });
        const [ax, ay] = def.anchor ?? [0.5, 1];
        items.push({ def, variant: 0, svgImage: img, vb, ax, ay, px: ph / vb[1], pw, ph, svg: innerSvg(markup) });
      }
    }
    // Shelf packing, tallest first.
    items.sort((a, b) => b.ph - a.ph);
    const canvas = typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(ATLAS, ATLAS) : Object.assign(document.createElement('canvas'), { width: ATLAS, height: ATLAS });
    const ctx = canvas.getContext('2d') as OffscreenCanvasRenderingContext2D | CanvasRenderingContext2D;
    let x = PAD, y = PAD, shelf = 0;
    const entries = new Map<string, AssetEntry>(), byAsset = new Map<string, AssetEntry[]>();
    for (const it of items) {
      if (x + it.pw + PAD > ATLAS) { x = PAD; y += shelf + PAD; shelf = 0; }
      if (y + it.ph + PAD > ATLAS) { console.warn('stamp atlas full; remaining assets skipped'); break; }
      ctx.save();
      ctx.translate(x, y);
      if (it.stamp) { ctx.scale(it.px, it.px); drawStamp(ctx, it.stamp, palette); }
      else if (it.svgImage) ctx.drawImage(it.svgImage, 0, 0);
      ctx.restore();
      const muPerUnit = it.stamp ? it.def.size / (GENERATORS[it.def.generator!].defaultSize * UNITS_PER_MAP_UNIT) : it.def.size / it.vb[1];
      const e: AssetEntry = {
        asset: it.def.id, variant: it.variant,
        u0: x / ATLAS, v0: y / ATLAS, u1: (x + it.vb[0] * it.px) / ATLAS, v1: (y + it.vb[1] * it.px) / ATLAS,
        w: it.vb[0] * muPerUnit, h: it.vb[1] * muPerUnit, ax: it.ax, ay: it.ay, category: it.def.category, viewBox: it.vb, svg: it.svg,
      };
      entries.set(`${e.asset}#${e.variant}`, e);
      let l = byAsset.get(e.asset); if (!l) byAsset.set(e.asset, (l = [])); l.push(e);
      x += it.pw + PAD; shelf = Math.max(shelf, it.ph);
    }
    for (const l of byAsset.values()) l.sort((a, b) => a.variant - b.variant);
    this.entries = entries; this.byAsset = byAsset; this.canvas = canvas;
    this.version++;
  }
}

/** Paints a procedural stamp with Canvas 2D (same shapes as its SVG form). */
export function drawStamp(ctx: CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D, stamp: Stamp, palette: Record<PaletteToken, string>): void {
  ctx.lineJoin = 'round'; ctx.lineCap = 'round';
  for (const sh of stamp.shapes) {
    const p = new Path2D(sh.d);
    ctx.globalAlpha = sh.opacity ?? 1;
    if (sh.fill !== 'none') { ctx.fillStyle = palette[sh.fill] ?? '#000'; ctx.fill(p); }
    if (sh.stroke !== 'none') { ctx.strokeStyle = palette[sh.stroke] ?? '#000'; ctx.lineWidth = sh.width ?? 1; ctx.stroke(p); }
  }
  ctx.globalAlpha = 1;
}

/** Replaces "token:<name>" colors in authored SVG with theme colors; returns markup and viewBox size. */
export function resolveSvg(svg: string, palette: Record<string, string>): { markup: string; vb: [number, number] } {
  const markup = svg.replace(/token:([a-zA-Z]+)/g, (_, t: string) => palette[t] ?? '#000');
  const m = /viewBox\s*=\s*"\s*[-\d.]+[\s,]+[-\d.]+[\s,]+([\d.]+)[\s,]+([\d.]+)\s*"/.exec(markup);
  const vb: [number, number] = m ? [parseFloat(m[1]), parseFloat(m[2])] : [100, 100];
  return { markup: markup.includes('xmlns') ? markup : markup.replace('<svg', '<svg xmlns="http://www.w3.org/2000/svg"'), vb };
}

const innerSvg = (markup: string) => markup.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('could not decode SVG asset')); };
    img.src = url;
  });
}
