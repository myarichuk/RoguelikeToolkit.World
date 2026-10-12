import { MAP_H, MAP_W, lonLatToMap } from '../core/geo';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { Theme } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import type { LabelEntity, RiverEntity, StampEntity } from '../doc/types';
import { DEPOSIT_MARKERS } from '../render/Overlay';
import type { TerrainPipeline } from '../render/TerrainPipeline';
import type { ViewSettings } from '../render/ViewSettings';
import type { FontLibrary } from '../text/FontLibrary';
import type { LabelCache } from '../text/LabelCache';
import { layoutLabel } from '../text/LabelLayout';
import type { ContourRequest, ContourResult } from '../../workers/export.worker';

export interface SvgExportOptions {
  /** Embed the colorized land raster (biome colors) clipped to the vector coast. */
  landTexture: boolean;
  ripples: boolean;
  rivers: boolean;
  stamps: boolean;
  labels: boolean;
  resources: boolean;
  graticule: boolean;
  /** Douglas-Peucker tolerance for contours, in field pixels. */
  tolerance: number;
}

export const svgOptionsFromView = (v: ViewSettings): SvgExportOptions => ({
  landTexture: true, ripples: v.layers.ripples, rivers: v.layers.rivers, stamps: v.layers.stamps, labels: v.layers.labels,
  resources: v.layers.resources && v.kinds.deposit, graticule: v.layers.graticule, tolerance: 0.35,
});

export interface SvgExportInput {
  doc: MapDocument;
  theme: Theme;
  view: ViewSettings;
  terrain: TerrainPipeline;
  library: AssetLibrary;
  labels: LabelCache;
  fonts: FontLibrary;
}

export interface SvgExportResult { svg: string; timings: Record<string, number>; }

/**
 * Vector export of the styled map. Geometry comes from the same sources the screen draws:
 * coast and ripple rings are iso-lines of the GPU signed distance field (contoured in a worker),
 * rivers are their ribbons, stamps are the atlas sources as <symbol>s, labels are the exact
 * glyph outlines the overlay fills. Layers are Inkscape layers; everything is clipped to the
 * map frame and wrapped across the antimeridian.
 */
export async function exportSvg(input: SvgExportInput, o: SvgExportOptions): Promise<SvgExportResult> {
  const { doc, theme, terrain } = input;
  const timings: Record<string, number> = {};
  const m = theme.map;
  let t = performance.now();
  const sdf = terrain.readSdf();
  const texture = o.landTexture ? await landTexturePng(terrain.readColor()) : null;
  timings.readbackMs = performance.now() - t;

  t = performance.now();
  const contours = await contourInWorker({
    sdf, w: terrain.fw, h: terrain.fh, scale: terrain.scale, ring: doc.settings.ring,
    rings: o.ripples ? m.rippleCount : 0, tolerance: o.tolerance,
  });
  timings.contourMs = performance.now() - t;

  t = performance.now();
  const out = new SvgWriter();
  out.raw(`<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" viewBox="0 0 ${MAP_W} ${MAP_H}" width="${MAP_W * 2}" height="${MAP_H * 2}">`);
  out.raw(`<title>${esc(doc.settings.title)}</title>`);
  out.raw('<defs>');
  out.raw(`<clipPath id="frame"><rect width="${MAP_W}" height="${MAP_H}"/></clipPath>`);
  // Shared geometry is defined once and referenced: the coast alone would otherwise be written
  // five times, every label twice (halo + fill).
  out.raw(`<path id="coast-line" d="${contours.coast}"/>`);
  out.raw(`<path id="land-shape" d="${contours.land}"/>`);
  out.raw('<clipPath id="land"><use href="#land-shape"/></clipPath>');
  const labelLayer = o.labels ? labels(input) : null;
  if (labelLayer) out.raw(labelLayer.defs);
  const symbols = o.stamps ? defineSymbols(out, doc.stamps.all(), input.library) : new Map<string, string>();
  out.raw('</defs>');
  out.raw('<g clip-path="url(#frame)">');

  out.layer('Sea', () => {
    out.raw(`<rect width="${MAP_W}" height="${MAP_H}" fill="${m.sea}"/>`);
    // Shallow-water halo: the coast stroked wide and soft (the screen uses the SDF directly).
    out.raw(`<use href="#coast-line" fill="none" stroke="${m.shallow}" stroke-width="${m.haloWidth * 2}" stroke-opacity="0.35" stroke-linejoin="round"/>`);
    out.raw(`<use href="#coast-line" fill="none" stroke="${m.shallow}" stroke-width="${m.haloWidth}" stroke-opacity="0.55" stroke-linejoin="round"/>`);
  });
  if (o.ripples && contours.ripples.length) {
    out.layer('Ripples', () => {
      contours.ripples.forEach((d, i) => {
        const a = m.rippleOpacity * (1 - i / (contours.ripples.length + 1));
        out.raw(`<path d="${d}" fill="none" stroke="${m.seaInk}" stroke-width="0.22" stroke-opacity="${a.toFixed(3)}"/>`);
      });
    });
  }
  out.layer('Land', () => {
    out.raw(`<use href="#land-shape" fill="${m.paper}"/>`);
    if (texture) out.raw(`<image clip-path="url(#land)" width="${MAP_W}" height="${MAP_H}" preserveAspectRatio="none" style="image-rendering:optimizeQuality" href="${texture}"/>`);
  });
  if (o.rivers) {
    out.layer('Rivers', () => {
      const d = [...doc.rivers.all()].map((r) => riverPath(r, m.riverWidth)).join('');
      if (d) out.raw(`<path d="${d}" fill="${m.river}" fill-rule="nonzero"/>`);
    });
  }
  out.layer('Coastline', () => {
    out.raw(`<use href="#coast-line" fill="none" stroke="${m.landInk}" stroke-width="${m.coastWidth}" stroke-linejoin="round" stroke-linecap="round"/>`);
  });
  if (o.graticule) out.layer('Graticule', () => out.raw(graticule(m.seaInk)));
  if (o.stamps && symbols.size) {
    out.layer('Stamps', () => {
      const list = [...doc.stamps.all()].sort((a, b) => a.y - b.y);
      for (const s of list) {
        const e = input.library.get(s.asset, s.variant), id = symbols.get(`${s.asset}#${e?.variant}`);
        if (!e || !id) continue;
        const w = e.w * s.scale, h = e.h * s.scale;
        for (const off of wrapOffsets(s.x, Math.max(w, h))) {
          const x = s.x + off;
          let tr = '';
          if (s.rotation) tr += ` rotate(${deg(s.rotation)} ${f(x)} ${f(s.y)})`;
          if (s.flip) tr += ` translate(${f(2 * x)} 0) scale(-1 1)`;
          out.raw(`<use href="#${id}" x="${f(x - e.ax * w)}" y="${f(s.y - e.ay * h)}" width="${f(w)}" height="${f(h)}"${tr ? ` transform="${tr.trim()}"` : ''}/>`);
        }
      }
    });
  }
  if (o.resources) out.layer('Resources', () => out.raw(resources(input, input.view)));
  if (labelLayer) out.layer('Labels', () => out.raw(labelLayer.body));
  out.raw('</g>');
  out.raw(`<rect width="${MAP_W}" height="${MAP_H}" fill="none" stroke="${m.frame}" stroke-width="3"/>`);
  out.raw('</svg>');
  timings.writeMs = performance.now() - t;
  timings.contourWorkerMs = contours.ms;
  return { svg: out.toString(), timings };
}

class SvgWriter {
  private parts: string[] = [];
  raw(s: string): void { this.parts.push(s); }
  layer(name: string, body: () => void): void {
    this.parts.push(`<g inkscape:groupmode="layer" inkscape:label="${esc(name)}" id="layer-${name.toLowerCase()}">`);
    body();
    this.parts.push('</g>');
  }
  toString(): string { return this.parts.join('\n'); }
}

const f = (v: number) => (Math.round(v * 100) / 100).toString();
const deg = (r: number) => f((r * 180) / Math.PI);
const esc = (s: string) => s.replace(/[<>&"']/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' })[c]!);

/** x offsets at which something of `reach` around x must be drawn to appear on both map edges. */
function wrapOffsets(x: number, reach: number): number[] {
  const out = [0];
  if (x - reach < 0) out.push(MAP_W);
  if (x + reach > MAP_W) out.push(-MAP_W);
  return out;
}

function defineSymbols(out: SvgWriter, stamps: Iterable<StampEntity>, lib: AssetLibrary): Map<string, string> {
  const ids = new Map<string, string>();
  for (const s of stamps) {
    const e = lib.get(s.asset, s.variant);
    if (!e) continue;
    const key = `${e.asset}#${e.variant}`;
    if (ids.has(key)) continue;
    const id = `s${ids.size}`;
    ids.set(key, id);
    out.raw(`<symbol id="${id}" viewBox="0 0 ${e.viewBox[0]} ${e.viewBox[1]}" preserveAspectRatio="none" overflow="visible"><desc>${esc(key)}</desc>${e.svg}</symbol>`);
  }
  return ids;
}

/** A river ribbon as one closed polygon (left bank down, right bank back), wrapped copies included. */
export function riverPath(r: RiverEntity, widthMul: number): string {
  const p = r.points, n = p.length / 3;
  if (n < 2) return '';
  const left: string[] = [], right: string[] = [];
  let lo = Infinity, hi = -Infinity;
  for (let i = 0; i < n; i++) { lo = Math.min(lo, p[i * 3]); hi = Math.max(hi, p[i * 3]); }
  let d = '';
  for (const off of [-MAP_W, 0, MAP_W]) {
    if (hi + off < 0 || lo + off > MAP_W) continue;
    left.length = right.length = 0;
    for (let i = 0; i < n; i++) {
      const a = Math.max(0, i - 1), b = Math.min(n - 1, i + 1);
      let tx = p[b * 3] - p[a * 3], ty = p[b * 3 + 1] - p[a * 3 + 1];
      const len = Math.hypot(tx, ty) || 1; tx /= len; ty /= len;
      const hw = Math.max(0.18, (p[i * 3 + 2] * widthMul) / 2);
      const x = p[i * 3] + off, y = p[i * 3 + 1];
      left.push(`${f(x - ty * hw)} ${f(y + tx * hw)}`);
      right.push(`${f(x + ty * hw)} ${f(y - tx * hw)}`);
    }
    d += `M${left.join('L')}L${right.reverse().join('L')}Z`;
  }
  return d;
}

function labels(input: SvgExportInput): { defs: string; body: string } {
  const { doc, theme, view } = input;
  const parts: string[] = [], defs: string[] = [];
  for (const l of doc.labels.all()) {
    if (!labelVisible(l, view)) continue;
    const r = input.labels.resolve(l, theme);
    if (!r || !r.geo.d) continue;
    const st = r.style;
    const reach = Math.max(r.geo.bounds.x1 - l.x, l.x - r.geo.bounds.x0);
    const id = `t${defs.length}`;
    defs.push(`<path id="${id}" d="${r.geo.d}"/>`);
    for (const off of wrapOffsets(l.x, reach)) {
      const tr = off ? ` transform="translate(${off} 0)"` : '';
      parts.push(`<g${tr}><title>${esc(l.text)}</title>`);
      if (st.haloWidth > 0) parts.push(`<use href="#${id}" fill="${st.halo}" stroke="${st.halo}" stroke-width="${f(st.haloWidth * 2)}" stroke-linejoin="round" stroke-opacity="0.85"/>`);
      parts.push(`<use href="#${id}" fill="${st.fill}"/></g>`);
    }
  }
  return { defs: defs.join('\n'), body: parts.join('\n') };
}

const labelVisible = (l: LabelEntity, view: ViewSettings) => {
  const kind = l.feature?.kind;
  return !(kind && kind in view.kinds && !view.kinds[kind as keyof typeof view.kinds]);
};

function resources(input: SvgExportInput, view: ViewSettings): string {
  const font = input.fonts.get('serif');
  const parts: string[] = [];
  for (const fe of input.doc.features.all()) {
    if (fe.kind !== 'deposit' || !fe.deposit || !view.deposits[fe.deposit.type]) continue;
    const mk = DEPOSIT_MARKERS[fe.deposit.type], r = 2.6 + fe.deposit.richness * 1.6;
    parts.push(`<circle cx="${f(fe.x)}" cy="${f(fe.y)}" r="${f(r)}" fill="${mk.color}" stroke="#fff" stroke-width="0.5"><title>${esc(fe.name || fe.deposit.type)}</title></circle>`);
    if (font) {
      const g = layoutLabel(font, { text: mk.symbol, size: r * 0.8, spacing: 0, uppercase: false, x: fe.x, y: fe.y + r * 0.4, angle: 0, curve: 0 });
      parts.push(`<path d="${g.d}" fill="#fff"/>`);
    }
  }
  return parts.join('\n');
}

function graticule(color: string): string {
  let d = '';
  for (let lon = -180; lon <= 180; lon += 15) { const x = lonLatToMap((lon * Math.PI) / 180, 0).x; d += `M${f(x)} 0V${MAP_H}`; }
  for (let lat = -75; lat <= 75; lat += 15) {
    const y = lonLatToMap(0, (lat * Math.PI) / 180).y;
    if (y > 0 && y < MAP_H) d += `M0 ${f(y)}H${MAP_W}`;
  }
  return `<path d="${d}" fill="none" stroke="${color}" stroke-width="0.35" stroke-opacity="0.35" stroke-dasharray="4 3"/>`;
}

/** RGBA8 (row 0 = north) -> PNG data URL. */
async function landTexturePng(rgba: Uint8Array): Promise<string> {
  const canvas = new OffscreenCanvas(MAP_W, MAP_H);
  const ctx = canvas.getContext('2d')!;
  const img = new ImageData(new Uint8ClampedArray(rgba.buffer, rgba.byteOffset, rgba.byteLength), MAP_W, MAP_H);
  for (let i = 3; i < img.data.length; i += 4) img.data[i] = 255;
  ctx.putImageData(img, 0, 0);
  const blob = await canvas.convertToBlob({ type: 'image/png' });
  return blobToDataUrl(blob);
}

export function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const r = new FileReader();
    r.onload = () => resolve(r.result as string);
    r.onerror = () => reject(r.error);
    r.readAsDataURL(blob);
  });
}

function contourInWorker(req: ContourRequest): Promise<ContourResult> {
  const w = new Worker(new URL('../../workers/export.worker.ts', import.meta.url), { type: 'module' });
  return new Promise<ContourResult>((resolve, reject) => {
    w.onmessage = (e: MessageEvent<ContourResult>) => { resolve(e.data); w.terminate(); };
    w.onerror = (e) => { reject(new Error(e.message || 'export worker failed')); w.terminate(); };
    w.postMessage(req, [req.sdf.buffer]);
  });
}
