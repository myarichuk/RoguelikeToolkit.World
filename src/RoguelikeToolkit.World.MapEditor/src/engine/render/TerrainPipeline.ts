import { MAP_H, MAP_W, type Rect } from '../core/geo';
import { FULLSCREEN_VS, bind, compile, framebuffer, texture, type Program } from './gl';
import { FS_COLORIZE, FS_FIELD, FS_JFA, FS_SDF, FS_SEED } from './shaders';

export interface TerrainParams { warp: number; detail: number; seed: number; }

/** JFA reach: steps 64..1 resolve distances up to 127 field px. Farther texels saturate. */
const JFA_FIRST_STEP = 64;
const JFA_REACH = JFA_FIRST_STEP * 2 - 1;

/**
 * GPU terrain: editable source rasters (elevation, biome) -> stylized field -> signed distance
 * field of the coast, plus warped land colors. Supports incremental updates: a brush dab
 * re-runs every pass only over its dirty region.
 *
 * Incremental JFA correctness: for a dirty rect D (field px) the field changes only inside D,
 * so the SDF changes only within D ⊕ reach. We re-seed and flood the region R = D ⊕ 2·reach
 * treating everything outside R as "no seed", and commit the SDF only on C = D ⊕ reach. Every
 * texel in C has its true nearest seed within `reach`, hence inside R, so the result equals a
 * full rebuild there.
 */
export class TerrainPipeline {
  readonly scale: number;
  readonly fw: number;
  readonly fh: number;
  readonly elevTex: WebGLTexture;
  readonly biomeTex: WebGLTexture;
  readonly paletteTex: WebGLTexture;
  readonly fieldTex: WebGLTexture;
  readonly sdfTex: WebGLTexture;
  readonly colorTex: WebGLTexture;
  private readonly seedA: WebGLTexture;
  private readonly seedB: WebGLTexture;
  private readonly fbField: WebGLFramebuffer;
  private readonly fbSdf: WebGLFramebuffer;
  private readonly fbColor: WebGLFramebuffer;
  private readonly fbA: WebGLFramebuffer;
  private readonly fbB: WebGLFramebuffer;
  private readonly progs: Record<'field' | 'color' | 'seed' | 'jfa' | 'sdf', Program>;
  private readonly vao: WebGLVertexArrayObject;
  params: TerrainParams = { warp: 6, detail: 0.05, seed: 0 };
  /** Last full/incremental build cost in ms (CPU-side submission + sync when measured). */
  lastMs = 0;

  constructor(private readonly gl: WebGL2RenderingContext, scale = 2) {
    this.scale = scale;
    this.fw = MAP_W * scale; this.fh = MAP_H * scale;
    const g = gl;
    this.vao = g.createVertexArray()!;
    this.progs = {
      field: compile(g, FULLSCREEN_VS, FS_FIELD), color: compile(g, FULLSCREEN_VS, FS_COLORIZE),
      seed: compile(g, FULLSCREEN_VS, FS_SEED), jfa: compile(g, FULLSCREEN_VS, FS_JFA), sdf: compile(g, FULLSCREEN_VS, FS_SDF),
    };
    this.elevTex = texture(g, { w: MAP_W, h: MAP_H, internal: g.R16F, format: g.RED, type: g.FLOAT, filter: g.LINEAR });
    this.biomeTex = texture(g, { w: MAP_W, h: MAP_H, internal: g.R8UI, format: g.RED_INTEGER, type: g.UNSIGNED_BYTE });
    this.paletteTex = texture(g, { w: 256, h: 1, internal: g.RGBA8, format: g.RGBA, type: g.UNSIGNED_BYTE });
    this.fieldTex = texture(g, { w: this.fw, h: this.fh, internal: g.R16F, format: g.RED, type: g.HALF_FLOAT, filter: g.LINEAR });
    this.sdfTex = texture(g, { w: this.fw, h: this.fh, internal: g.R16F, format: g.RED, type: g.HALF_FLOAT, filter: g.LINEAR });
    this.colorTex = texture(g, { w: MAP_W, h: MAP_H, internal: g.RGBA8, format: g.RGBA, type: g.UNSIGNED_BYTE, filter: g.LINEAR });
    this.seedA = texture(g, { w: this.fw, h: this.fh, internal: g.RG16F, format: g.RG, type: g.HALF_FLOAT });
    this.seedB = texture(g, { w: this.fw, h: this.fh, internal: g.RG16F, format: g.RG, type: g.HALF_FLOAT });
    this.fbField = framebuffer(g, this.fieldTex);
    this.fbSdf = framebuffer(g, this.sdfTex);
    this.fbColor = framebuffer(g, this.colorTex);
    this.fbA = framebuffer(g, this.seedA);
    this.fbB = framebuffer(g, this.seedB);
  }

  /** Uploads a region of a source raster (map units). Full upload when rect is omitted. */
  uploadElevation(data: Float32Array, rect?: Rect): void { this.upload(this.elevTex, data, this.gl.RED, this.gl.FLOAT, rect); }
  uploadBiome(data: Uint8Array, rect?: Rect): void { this.upload(this.biomeTex, data, this.gl.RED_INTEGER, this.gl.UNSIGNED_BYTE, rect); }

  uploadPalette(rgba: Uint8Array): void {
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, this.paletteTex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 256, 1, gl.RGBA, gl.UNSIGNED_BYTE, rgba);
  }

  private upload(tex: WebGLTexture, data: Float32Array | Uint8Array, format: number, type: number, rect?: Rect): void {
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    if (!rect) { gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, MAP_W, MAP_H, format, type, data); return; }
    // Split at the antimeridian; rows go through UNPACK_ROW_LENGTH so no CPU copy is needed.
    const y0 = Math.max(0, Math.floor(rect.y0)), y1 = Math.min(MAP_H, Math.ceil(rect.y1));
    if (y1 <= y0) return;
    gl.pixelStorei(gl.UNPACK_ROW_LENGTH, MAP_W);
    for (const [x0, x1] of spans(rect.x0, rect.x1, MAP_W)) {
      gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, x0);
      gl.pixelStorei(gl.UNPACK_SKIP_ROWS, y0);
      gl.texSubImage2D(gl.TEXTURE_2D, 0, x0, y0, x1 - x0, y1 - y0, format, type, data);
    }
    gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
    gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, 0);
    gl.pixelStorei(gl.UNPACK_SKIP_ROWS, 0);
  }

  /** Re-derives field, SDF and colors over a dirty map rect (or everything). */
  rebuild(dirty?: Rect, what: { field?: boolean; color?: boolean } = { field: true, color: true }): void {
    const t0 = performance.now();
    const gl = this.gl;
    const s = this.scale;
    // How far the warp can move a sample: the field at p depends on elevation at warp(p).
    const margin = this.params.warp * 1.2 + 2;
    const full = !dirty || dirty.x1 - dirty.x0 + 2 * margin >= MAP_W;
    const mapRect: Rect = full
      ? { x0: 0, y0: 0, x1: MAP_W, y1: MAP_H }
      : { x0: dirty!.x0 - margin, y0: Math.max(0, dirty!.y0 - margin), x1: dirty!.x1 + margin, y1: Math.min(MAP_H, dirty!.y1 + margin) };
    const common = { uMap: [MAP_W, MAP_H] as [number, number], uWarp: this.params.warp, uSeed: this.params.seed % 1000 };
    gl.bindVertexArray(this.vao);
    gl.disable(gl.BLEND);
    gl.enable(gl.SCISSOR_TEST);

    if (what.color !== false) {
      this.pass(this.fbColor, MAP_W, MAP_H, mapRect, () =>
        bind(gl, this.progs.color, common, { uBiome: this.biomeTex, uPalette: this.paletteTex }));
    }
    if (what.field !== false) {
      const fsize = { i: [this.fw, this.fh] };
      const D = scaleRect(mapRect, s);
      const R = grow(D, 2 * JFA_REACH, this.fw, this.fh);
      const C = grow(D, JFA_REACH, this.fw, this.fh);
      // A region crossing the antimeridian floods a full-width strip with x wrapping; the
      // commit region C stays tight either way (pass() splits it at the edge).
      const wrap = R.x0 < 0 || R.x1 > this.fw;
      const rRect = wrap ? { x0: 0, y0: R.y0, x1: this.fw, y1: R.y1 } : R;
      this.pass(this.fbField, this.fw, this.fh, D, () =>
        bind(gl, this.progs.field, { ...common, uScale: s, uDetail: this.params.detail }, { uElev: this.elevTex }));
      this.pass(this.fbA, this.fw, this.fh, rRect, () => bind(gl, this.progs.seed, { uSize: fsize }, { uField: this.fieldTex }));
      let cur = { t: this.seedA, f: this.fbA }, nxt = { t: this.seedB, f: this.fbB };
      for (let step = JFA_FIRST_STEP; step >= 1; step >>= 1) {
        this.pass(nxt.f, this.fw, this.fh, rRect, () => bind(gl, this.progs.jfa,
          { uSize: fsize, uStep: { i: step }, uRect: { i: [rRect.x0, rRect.y0, rRect.x1, rRect.y1] }, uWrap: wrap }, { uSeeds: cur.t }));
        [cur, nxt] = [nxt, cur];
      }
      this.pass(this.fbSdf, this.fw, this.fh, C, () =>
        bind(gl, this.progs.sdf, { uScale: s, uMaxDist: JFA_REACH / s }, { uSeeds: cur.t, uField: this.fieldTex }));
    }
    gl.disable(gl.SCISSOR_TEST);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    this.lastMs = performance.now() - t0;
  }

  /** Runs a fullscreen pass scissored to `rect` (x may extend past the edges: split and wrapped). */
  private pass(fb: WebGLFramebuffer, w: number, h: number, rect: Rect, setup: () => void): void {
    const gl = this.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
    gl.viewport(0, 0, w, h);
    setup();
    const y0 = Math.max(0, Math.floor(rect.y0)), y1 = Math.min(h, Math.ceil(rect.y1));
    for (const [x0, x1] of spans(rect.x0, rect.x1, w)) {
      gl.scissor(x0, y0, x1 - x0, y1 - y0);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    }
  }

  /** Signed distance (mu, + land) at field resolution; row 0 = north. For vector export. */
  readSdf(): Float32Array {
    const gl = this.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbSdf);
    const out = new Float32Array(this.fw * this.fh);
    const rgba = new Float32Array(this.fw * this.fh * 4);
    gl.readPixels(0, 0, this.fw, this.fh, gl.RGBA, gl.FLOAT, rgba);
    for (let i = 0; i < out.length; i++) out[i] = rgba[i * 4];
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return out;
  }

  /** Land colors at map resolution (RGBA8, row 0 = north). For raster layers in exports. */
  readColor(): Uint8Array {
    const gl = this.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbColor);
    const out = new Uint8Array(MAP_W * MAP_H * 4);
    gl.readPixels(0, 0, MAP_W, MAP_H, gl.RGBA, gl.UNSIGNED_BYTE, out);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return out;
  }

  /** Blocks until the GPU has finished queued work (for honest timings). */
  finish(): number {
    const gl = this.gl, t0 = performance.now();
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbSdf);
    const px = new Float32Array(4);
    gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.FLOAT, px);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return performance.now() - t0;
  }

  dispose(): void {
    const gl = this.gl;
    for (const t of [this.elevTex, this.biomeTex, this.paletteTex, this.fieldTex, this.sdfTex, this.colorTex, this.seedA, this.seedB]) gl.deleteTexture(t);
    for (const f of [this.fbField, this.fbSdf, this.fbColor, this.fbA, this.fbB]) gl.deleteFramebuffer(f);
    for (const p of Object.values(this.progs)) gl.deleteProgram(p.p);
    gl.deleteVertexArray(this.vao);
  }
}

/** [x0, x1) split into in-range spans of a width-periodic axis. */
export function spans(x0: number, x1: number, w: number): [number, number][] {
  const a = Math.floor(x0), b = Math.ceil(x1);
  if (b - a >= w) return [[0, w]];
  const s = ((a % w) + w) % w, e = s + (b - a);
  return e <= w ? [[s, e]] : [[s, w], [0, e - w]];
}

const scaleRect = (r: Rect, s: number): Rect => ({ x0: r.x0 * s, y0: r.y0 * s, x1: r.x1 * s, y1: r.y1 * s });

function grow(r: Rect, by: number, w: number, h: number): Rect {
  const g = { x0: r.x0 - by, y0: Math.max(0, r.y0 - by), x1: r.x1 + by, y1: Math.min(h, r.y1 + by) };
  return g.x1 - g.x0 >= w ? { x0: 0, y0: g.y0, x1: w, y1: g.y1 } : g;
}
