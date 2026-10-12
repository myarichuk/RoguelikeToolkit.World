import { MAP_H, MAP_W, rectUnion, type Rect } from '../core/geo';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { BiomeDef, Theme } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import { composeBiomes } from '../world/landuse';
import { Camera } from './Camera';
import { FULLSCREEN_VS, bind, compile, rgb, texture, type Program } from './gl';
import { RiverLayer } from './RiverLayer';
import { FS_DISPLAY } from './shaders';
import { StampLayer } from './StampLayer';
import { TerrainPipeline } from './TerrainPipeline';
import { BASE_MODE_INDEX, DEFAULT_VIEW, displayFlags, type ViewSettings } from './ViewSettings';

export interface FrameStats { frameMs: number; terrainMs: number; stamps: number; }

/**
 * The GPU side of the editor. Observes a MapDocument and draws it: terrain passes (incremental),
 * the display shader (view modes/overlays), rivers and stamps. Work is coalesced per animation
 * frame: any number of raster edits between two frames cost one upload + one rebuild of the
 * union of their dirty rects.
 */
export class MapRenderer {
  readonly camera = new Camera();
  readonly gl: WebGL2RenderingContext;
  readonly terrain: TerrainPipeline;
  private readonly display: Program;
  private readonly stamps: StampLayer;
  private readonly rivers: RiverLayer;
  private readonly vao: WebGLVertexArrayObject;
  private readonly plateTex: WebGLTexture;
  private readonly maskTex: WebGLTexture;
  private readonly vividTex: WebGLTexture;
  private readonly mask = new Uint8Array(MAP_W * MAP_H * 2);
  private hasMask = false;
  private doc: MapDocument | null = null;
  private lib: AssetLibrary | null = null;
  private theme: Theme | null = null;
  private view: ViewSettings = DEFAULT_VIEW;
  /** What the GPU draws: the biome raster with land use composited on top (the document keeps them apart). */
  private readonly drawn = new Uint8Array(MAP_W * MAP_H);
  private dirty: { elevation: Rect | null; biome: Rect | null; color: boolean; full: boolean } = { elevation: null, biome: null, color: false, full: false };
  private stampsDirty = true;
  private riversDirty = true;
  private raf = 0;
  private unsubscribe: (() => void)[] = [];
  /** Called after the GL frame, for the 2D overlay. */
  onAfterFrame: (() => void) | null = null;
  stats: FrameStats = { frameMs: 0, terrainMs: 0, stamps: 0 };

  constructor(readonly canvas: HTMLCanvasElement, fieldScale = 2) {
    const gl = canvas.getContext('webgl2', { antialias: false, premultipliedAlpha: true, preserveDrawingBuffer: true });
    if (!gl) throw new Error('WebGL2 is not available in this browser.');
    if (!gl.getExtension('EXT_color_buffer_float')) throw new Error('EXT_color_buffer_float is required (float render targets).');
    gl.getExtension('EXT_color_buffer_half_float');
    this.gl = gl;
    this.terrain = new TerrainPipeline(gl, fieldScale);
    this.display = compile(gl, FULLSCREEN_VS, FS_DISPLAY);
    this.stamps = new StampLayer(gl);
    this.rivers = new RiverLayer(gl);
    this.vao = gl.createVertexArray()!;
    this.plateTex = texture(gl, { w: MAP_W, h: MAP_H, internal: gl.R32UI, format: gl.RED_INTEGER, type: gl.UNSIGNED_INT });
    this.maskTex = texture(gl, { w: MAP_W, h: MAP_H, internal: gl.RG8, format: gl.RG, type: gl.UNSIGNED_BYTE, filter: gl.LINEAR });
    this.vividTex = texture(gl, { w: 256, h: 1, internal: gl.RGBA8, format: gl.RGBA, type: gl.UNSIGNED_BYTE });
  }

  attach(doc: MapDocument, lib: AssetLibrary): void {
    this.unsubscribe.forEach((u) => u());
    this.doc = doc; this.lib = lib;
    this.terrain.params = { warp: doc.settings.warp, detail: doc.settings.detail, seed: doc.settings.seed };
    this.terrain.uploadElevation(doc.elevation);
    composeBiomes(doc.biome, doc.landuse, this.drawn, MAP_W);
    this.terrain.uploadBiome(this.drawn);
    this.uploadPlates(doc);
    this.clearMask();
    this.dirty = { elevation: null, biome: null, color: true, full: true };
    this.stampsDirty = this.riversDirty = true;
    this.unsubscribe = [
      doc.events.on('raster', ({ layer, rect }) => {
        if (layer !== 'elevation' && layer !== 'biome' && layer !== 'landuse') return; // climate paint is overlay-only
        const key = layer === 'elevation' ? 'elevation' : 'biome'; // land use rides the biome texture
        this.dirty[key] = rectUnion(this.dirty[key], rect);
        this.requestRender();
      }),
      doc.events.on('settings', (s) => {
        const p = this.terrain.params;
        if (p.warp !== s.warp || p.detail !== s.detail) {
          this.terrain.params = { warp: s.warp, detail: s.detail, seed: s.seed };
          this.dirty.full = true;
        }
        this.requestRender();
      }),
      doc.stamps.events.on('change', () => { this.stampsDirty = true; this.requestRender(); }),
      doc.rivers.events.on('change', () => { this.riversDirty = true; this.requestRender(); }),
    ];
    this.requestRender();
  }

  setTheme(theme: Theme, biomes: BiomeDef[]): void {
    this.theme = theme;
    const styled = new Uint8Array(256 * 4), vivid = new Uint8Array(256 * 4);
    const fallback = rgb(theme.biomes['1'] ?? '#d8cfa5');
    for (let i = 0; i < 256; i++) {
      const c = theme.biomes[String(i)] ? rgb(theme.biomes[String(i)]) : fallback;
      styled.set([c[0] * 255, c[1] * 255, c[2] * 255, 255], i * 4);
    }
    for (const b of biomes) { const c = rgb(b.vivid); vivid.set([c[0] * 255, c[1] * 255, c[2] * 255, 255], b.id * 4); }
    this.terrain.uploadPalette(styled);
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, this.vividTex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 256, 1, gl.RGBA, gl.UNSIGNED_BYTE, vivid);
    this.dirty.color = true;
    this.stampsDirty = true;
    this.requestRender();
  }

  setView(view: ViewSettings): void { this.view = view; this.requestRender(); }

  /** Marks the atlas as changed (theme switch, pack change). */
  assetsChanged(): void { this.stampsDirty = true; this.requestRender(); }

  /** Hides stamps from the GPU batch (a tool draws them itself while dragging). */
  setHiddenStamps(ids: Iterable<string>): void { this.stamps.hidden = new Set(ids); this.stampsDirty = true; this.requestRender(); }

  /** Selection (channel r) and hover (channel g) masks from tile sets; null clears. */
  setMasks(selected: Iterable<number> | null, hovered: Iterable<number> | null): void {
    const doc = this.doc;
    if (!doc) return;
    const sel = toLookup(selected, doc.world.tilePlate.length), hov = toLookup(hovered, doc.world.tilePlate.length);
    const m = this.mask, tile = doc.world.tile;
    this.hasMask = !!(sel || hov);
    if (!this.hasMask) { this.clearMask(); this.requestRender(); return; }
    for (let i = 0; i < tile.length; i++) {
      const t = tile[i];
      m[i * 2] = sel && t >= 0 && sel[t] ? 255 : 0;
      m[i * 2 + 1] = hov && t >= 0 && hov[t] ? 255 : 0;
    }
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, this.maskTex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, MAP_W, MAP_H, gl.RG, gl.UNSIGNED_BYTE, m);
    this.requestRender();
  }

  resize(cssW: number, cssH: number, dpr: number): void {
    const w = Math.max(1, Math.round(cssW * dpr)), h = Math.max(1, Math.round(cssH * dpr));
    const cam = this.camera;
    const first = cam.width <= 1;
    if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
    cam.width = w; cam.height = h; cam.dpr = dpr;
    if (first) cam.fit(); else cam.constrain();
    this.requestRender();
  }

  requestRender(): void {
    if (this.raf) return;
    this.raf = requestAnimationFrame(() => { this.raf = 0; this.frame(); });
  }

  /** Synchronous render (exports, tests). */
  renderNow(): void { if (this.raf) { cancelAnimationFrame(this.raf); this.raf = 0; } this.frame(); }

  private frame(): void {
    const doc = this.doc, t0 = performance.now();
    if (!doc || !this.theme) return;
    this.flushTerrain();
    if (this.lib) this.stamps.syncAtlas(this.lib);
    if (this.stampsDirty && this.lib) { this.stamps.rebuild(doc.stamps.all(), this.lib); this.stampsDirty = false; this.stats.stamps = doc.stamps.size; }
    if (this.riversDirty) { this.rivers.rebuild(doc.rivers.all()); this.riversDirty = false; }

    const gl = this.gl, cam = this.camera, th = this.theme.map, v = this.view;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, cam.width, cam.height);
    gl.bindVertexArray(this.vao);
    bind(gl, this.display, {
      uMap: [MAP_W, MAP_H], uCanvas: [cam.width, cam.height], uCenter: [cam.cx, cam.cy], uZoom: cam.zoom,
      uFieldSize: [this.terrain.fw, this.terrain.fh], uWarp: 0, uSeed: 0,
      uMode: { i: BASE_MODE_INDEX[v.base] }, uFlags: { i: displayFlags(v, this.hasMask) }, uPlateTopo: v.plateTopo,
      uPaper: rgb(th.paper), uSea: rgb(th.sea), uShallow: rgb(th.shallow), uSeaInk: rgb(th.seaInk), uLandInk: rgb(th.landInk),
      uFrame: rgb(th.frame), uAccent: rgb(this.theme.ui?.['--accent'] ?? '#d08a2a'),
      uCoastW: th.coastWidth, uHalo: th.haloWidth, uRing: doc.settings.ring, uRings: th.rippleCount, uRippleOpacity: th.rippleOpacity,
      uShade: th.hillshade, uGrain: th.grain, uStains: th.stains, uInnerCoast: th.innerCoast,
    }, {
      uSdf: this.terrain.sdfTex, uField: this.terrain.fieldTex, uColor: this.terrain.colorTex, uElev: this.terrain.elevTex,
      uVivid: this.vividTex, uMask: this.maskTex, uBiome: this.terrain.biomeTex, uPlate: this.plateTex,
    });
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    if (v.layers.rivers) this.rivers.draw(cam, th.river, th.riverWidth);
    if (v.layers.stamps && v.base === 'styled') this.stamps.draw(cam);
    this.stats.frameMs = performance.now() - t0;
    this.onAfterFrame?.();
  }

  private flushTerrain(): void {
    const d = this.dirty, doc = this.doc!;
    if (!d.full && !d.elevation && !d.biome && !d.color) return;
    const t0 = performance.now();
    if (d.full) {
      this.terrain.rebuild();
    } else {
      if (d.elevation) { this.terrain.uploadElevation(doc.elevation, d.elevation); this.terrain.rebuild(d.elevation, { field: true, color: false }); }
      if (d.biome) { composeBiomes(doc.biome, doc.landuse, this.drawn, MAP_W, d.biome); this.terrain.uploadBiome(this.drawn, d.biome); this.terrain.rebuild(d.biome, { field: false, color: true }); }
      if (d.color && !d.biome) this.terrain.rebuild(undefined, { field: false, color: true });
    }
    this.dirty = { elevation: null, biome: null, color: false, full: false };
    this.stats.terrainMs = performance.now() - t0;
  }

  private uploadPlates(doc: MapDocument): void {
    const tile = doc.world.tile, tp = doc.world.tilePlate;
    const px = new Uint32Array(MAP_W * MAP_H);
    if (tp.length) for (let i = 0; i < px.length; i++) px[i] = tile[i] >= 0 ? tp[tile[i]] : 0;
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, this.plateTex);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, MAP_W, MAP_H, gl.RED_INTEGER, gl.UNSIGNED_INT, px);
  }

  private clearMask(): void {
    this.mask.fill(0);
    this.hasMask = false;
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, this.maskTex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, MAP_W, MAP_H, gl.RG, gl.UNSIGNED_BYTE, this.mask);
  }

  dispose(): void {
    this.unsubscribe.forEach((u) => u());
    if (this.raf) cancelAnimationFrame(this.raf);
    this.terrain.dispose();
  }
}

function toLookup(tiles: Iterable<number> | null, n: number): Uint8Array | null {
  if (!tiles) return null;
  const out = new Uint8Array(Math.max(n, 1));
  let any = false;
  for (const t of tiles) if (t >= 0 && t < out.length) { out[t] = 1; any = true; }
  return any ? out : null;
}
