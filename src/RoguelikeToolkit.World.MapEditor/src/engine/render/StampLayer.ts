import type { AssetLibrary } from '../content/AssetLibrary';
import { hashString } from '../core/rng';
import type { StampEntity } from '../doc/types';
import type { Camera } from './Camera';
import { bind, compile, texture, type Program } from './gl';
import { FS_STAMP, VS_STAMP } from './shaders';

const FLOATS = 15; // x y w h | ax ay rot flip | u0 v0 u1 v1 | alpha | lod class, lod key

/** Level-of-detail class per asset category (see `lodShare` in the stamp shader). */
const LOD_CLASS: Record<string, number> = { tree: 1, vegetation: 1, terrain: 1, mountain: 2, hill: 2, water: 3 };

/**
 * Where a stamp falls in its class's LOD order (lower survives zooming out longer). Relief ranks by
 * scale, which the relief placer gives its most prominent peaks, with a little hash so equal sizes
 * do not pop together; everything else is a stable hash of the id, so a forest thins evenly.
 */
export function lodKey(s: StampEntity, cls: number): number {
  const h = (hashString(s.id) % 10007) / 10007;
  if (cls !== 2) return h;
  const importance = Math.min(1, Math.max(0, (s.scale - 0.55) / 0.95));
  return (1 - importance) * 0.9 + h * 0.1;
}

/**
 * All stamps in one instanced draw per visible map copy. The instance buffer is rebuilt when the
 * stamp set or the atlas changes (O(n), ~5 ms for 50k stamps) and sorted by anchor y, so nearer
 * (lower) stamps overlap farther ones, as on a hand-drawn map.
 */
export class StampLayer {
  private readonly prog: Program;
  private readonly vao: WebGLVertexArrayObject;
  private readonly instanceBuf: WebGLBuffer;
  private atlasTex: WebGLTexture | null = null;
  private atlasVersion = -1;
  private count = 0;
  /** Stamp ids hidden while being dragged by a tool (drawn by the overlay instead). */
  hidden = new Set<string>();

  constructor(private readonly gl: WebGL2RenderingContext) {
    this.prog = compile(gl, VS_STAMP, FS_STAMP);
    this.vao = gl.createVertexArray()!;
    gl.bindVertexArray(this.vao);
    const quad = gl.createBuffer()!;
    gl.bindBuffer(gl.ARRAY_BUFFER, quad);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 1, 1]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    this.instanceBuf = gl.createBuffer()!;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.instanceBuf);
    const stride = FLOATS * 4;
    const attr = (loc: number, size: number, offset: number) => {
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, size, gl.FLOAT, false, stride, offset * 4);
      gl.vertexAttribDivisor(loc, 1);
    };
    attr(1, 4, 0); attr(2, 4, 4); attr(3, 4, 8); attr(4, 1, 12); attr(5, 2, 13);
    gl.bindVertexArray(null);
  }

  /** Uploads the atlas if the library rebuilt it. */
  syncAtlas(lib: AssetLibrary): void {
    if (lib.version === this.atlasVersion || !lib.canvas) return;
    const gl = this.gl;
    if (this.atlasTex) gl.deleteTexture(this.atlasTex);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    this.atlasTex = texture(gl, { w: lib.canvas.width, h: lib.canvas.height, internal: gl.RGBA8, format: gl.RGBA, type: gl.UNSIGNED_BYTE, filter: gl.LINEAR_MIPMAP_LINEAR, wrapX: gl.CLAMP_TO_EDGE });
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, gl.RGBA, gl.UNSIGNED_BYTE, lib.canvas as TexImageSource);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    this.atlasVersion = lib.version;
  }

  /** `absent` are stamps that do not exist yet at the history turn on view; like `hidden`, they are left out of the batch. */
  rebuild(stamps: Iterable<StampEntity>, lib: AssetLibrary, absent: ReadonlySet<string> = new Set()): void {
    const list: { s: StampEntity; y: number }[] = [];
    for (const s of stamps) if (!this.hidden.has(s.id) && !absent.has(s.id)) list.push({ s, y: s.y });
    list.sort((a, b) => a.y - b.y);
    const data = new Float32Array(list.length * FLOATS);
    let n = 0;
    for (const { s } of list) {
      const e = lib.get(s.asset, s.variant);
      if (!e) continue;
      const o = n * FLOATS;
      data[o] = s.x; data[o + 1] = s.y; data[o + 2] = e.w * s.scale; data[o + 3] = e.h * s.scale;
      data[o + 4] = e.ax; data[o + 5] = e.ay; data[o + 6] = s.rotation; data[o + 7] = s.flip ? -1 : 1;
      data[o + 8] = e.u0; data[o + 9] = e.v0; data[o + 10] = e.u1; data[o + 11] = e.v1; data[o + 12] = 1;
      const cls = LOD_CLASS[e.category] ?? 0;
      data[o + 13] = cls; data[o + 14] = lodKey(s, cls);
      n++;
    }
    const gl = this.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.instanceBuf);
    gl.bufferData(gl.ARRAY_BUFFER, data.subarray(0, n * FLOATS), gl.DYNAMIC_DRAW);
    this.count = n;
  }

  draw(cam: Camera): void {
    if (!this.count || !this.atlasTex) return;
    const gl = this.gl;
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.bindVertexArray(this.vao);
    bind(gl, this.prog, { uCanvas: [cam.width, cam.height], uCenter: [cam.cx, cam.cy], uZoom: cam.zoom, uDpr: cam.dpr }, { uAtlas: this.atlasTex });
    for (const off of cam.copies(64)) {
      gl.uniform1f(this.prog.u.uOffset, off);
      gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, this.count);
    }
    gl.bindVertexArray(null);
    gl.disable(gl.BLEND);
  }
}
