import type { RiverEntity } from '../doc/types';
import type { Camera } from './Camera';
import { bind, compile, rgb, type Program } from './gl';
import { FS_RIVER, VS_RIVER } from './shaders';

/** Default width (map units) of the inked banks either side of a river's water. */
export const RIVER_INK_MU = 0.3;

/** Builds ribbon geometry for river polylines (x, y, w triples): two vertices per point, miter normals. */
export function riverMesh(rivers: Iterable<RiverEntity>): { verts: Float32Array; index: Uint32Array } {
  const v: number[] = [];
  const idx: number[] = [];
  for (const r of rivers) {
    const p = r.points, n = p.length / 3;
    if (n < 2) continue;
    const base = v.length / 6;
    for (let i = 0; i < n; i++) {
      const a = Math.max(0, i - 1), b = Math.min(n - 1, i + 1);
      let tx = p[b * 3] - p[a * 3], ty = p[b * 3 + 1] - p[a * 3 + 1];
      const len = Math.hypot(tx, ty) || 1; tx /= len; ty /= len;
      const nx = -ty, ny = tx;
      const hw = p[i * 3 + 2] / 2;
      v.push(p[i * 3], p[i * 3 + 1], nx, ny, hw, -1, p[i * 3], p[i * 3 + 1], nx, ny, hw, 1);
      if (i < n - 1) {
        const k = base + i * 2;
        idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2);
      }
    }
  }
  return { verts: new Float32Array(v), index: new Uint32Array(idx) };
}

export class RiverLayer {
  private readonly prog: Program;
  private readonly vao: WebGLVertexArrayObject;
  private readonly vbuf: WebGLBuffer;
  private readonly ibuf: WebGLBuffer;
  private count = 0;

  constructor(private readonly gl: WebGL2RenderingContext) {
    this.prog = compile(gl, VS_RIVER, FS_RIVER);
    this.vao = gl.createVertexArray()!;
    gl.bindVertexArray(this.vao);
    this.vbuf = gl.createBuffer()!;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.vbuf);
    const stride = 6 * 4;
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 8);
    gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 1, gl.FLOAT, false, stride, 16);
    gl.enableVertexAttribArray(3); gl.vertexAttribPointer(3, 1, gl.FLOAT, false, stride, 20);
    this.ibuf = gl.createBuffer()!;
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.ibuf);
    gl.bindVertexArray(null);
  }

  rebuild(rivers: Iterable<RiverEntity>): void {
    const { verts, index } = riverMesh(rivers);
    const gl = this.gl;
    gl.bindVertexArray(this.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.vbuf);
    gl.bufferData(gl.ARRAY_BUFFER, verts, gl.DYNAMIC_DRAW);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.ibuf);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, index, gl.DYNAMIC_DRAW);
    gl.bindVertexArray(null);
    this.count = index.length;
  }

  /** Banks in `ink` (`inkMu` wide, at least a hairline), water in `color` inside them. */
  draw(cam: Camera, color: string, ink: string, inkMu: number, widthScale: number): void {
    if (!this.count) return;
    const gl = this.gl;
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.bindVertexArray(this.vao);
    const inkPx = Math.max(0.75 * cam.dpr, inkMu * cam.zoom);
    for (const pass of [0, 1]) {
      bind(gl, this.prog, { uCanvas: [cam.width, cam.height], uCenter: [cam.cx, cam.cy], uZoom: cam.zoom, uWidthScale: widthScale, uColor: rgb(pass ? color : ink), uInkPx: inkPx, uPass: { i: pass } }, {});
      // Rivers are stored unwrapped and may span past the map edge: draw one extra copy each side.
      for (const off of cam.copies(1024)) {
        gl.uniform1f(this.prog.u.uOffset, off);
        gl.drawElements(gl.TRIANGLES, this.count, gl.UNSIGNED_INT, 0);
      }
    }
    gl.bindVertexArray(null);
    gl.disable(gl.BLEND);
  }

}
