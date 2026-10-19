import type { RoadEntity } from '../doc/types';
import type { Camera } from './Camera';
import { bind, compile, rgb, type Program } from './gl';
import { FS_ROAD, VS_ROAD } from './shaders';

const FLOATS = 8;

const styleOf = (r: RoadEntity): number => (r.severed >= 0 ? 2 : r.kind === 'trunk' ? 0 : 1);

/** Ribbon geometry for road polylines (x, y, w triples): two vertices per point, miter normals, arc length and a style. */
export function roadMesh(roads: Iterable<RoadEntity>): { verts: Float32Array; index: Uint32Array } {
  const v: number[] = [];
  const idx: number[] = [];
  for (const r of roads) {
    const p = r.points, n = p.length / 3;
    if (n < 2) continue;
    const base = v.length / FLOATS, style = styleOf(r);
    let along = 0;
    for (let i = 0; i < n; i++) {
      if (i) along += Math.hypot(p[i * 3] - p[i * 3 - 3], p[i * 3 + 1] - p[i * 3 - 2]);
      const a = Math.max(0, i - 1), b = Math.min(n - 1, i + 1);
      let tx = p[b * 3] - p[a * 3], ty = p[b * 3 + 1] - p[a * 3 + 1];
      const len = Math.hypot(tx, ty) || 1; tx /= len; ty /= len;
      const hw = p[i * 3 + 2] / 2;
      for (const side of [-1, 1]) v.push(p[i * 3], p[i * 3 + 1], -ty, tx, hw, side, along, style);
      if (i < n - 1) {
        const k = base + i * 2;
        idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2);
      }
    }
  }
  return { verts: new Float32Array(v), index: new Uint32Array(idx) };
}

export class RoadLayer {
  private readonly prog: Program;
  private readonly vao: WebGLVertexArrayObject;
  private readonly vbuf: WebGLBuffer;
  private readonly ibuf: WebGLBuffer;
  private count = 0;

  constructor(private readonly gl: WebGL2RenderingContext) {
    this.prog = compile(gl, VS_ROAD, FS_ROAD);
    this.vao = gl.createVertexArray()!;
    gl.bindVertexArray(this.vao);
    this.vbuf = gl.createBuffer()!;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.vbuf);
    const stride = FLOATS * 4;
    [[2, 0], [2, 8], [1, 16], [1, 20], [1, 24], [1, 28]].forEach(([size, off], i) => {
      gl.enableVertexAttribArray(i); gl.vertexAttribPointer(i, size, gl.FLOAT, false, stride, off);
    });
    this.ibuf = gl.createBuffer()!;
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.ibuf);
    gl.bindVertexArray(null);
  }

  rebuild(roads: Iterable<RoadEntity>): void {
    const { verts, index } = roadMesh(roads);
    const gl = this.gl;
    gl.bindVertexArray(this.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.vbuf);
    gl.bufferData(gl.ARRAY_BUFFER, verts, gl.DYNAMIC_DRAW);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.ibuf);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, index, gl.DYNAMIC_DRAW);
    gl.bindVertexArray(null);
    this.count = index.length;
  }

  draw(cam: Camera, color: string, widthScale: number): void {
    if (!this.count) return;
    const gl = this.gl;
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.bindVertexArray(this.vao);
    bind(gl, this.prog, { uCanvas: [cam.width, cam.height], uCenter: [cam.cx, cam.cy], uZoom: cam.zoom, uWidthScale: widthScale, uColor: rgb(color) }, {});
    for (const off of cam.copies(1024)) {
      gl.uniform1f(this.prog.u.uOffset, off);
      gl.drawElements(gl.TRIANGLES, this.count, gl.UNSIGNED_INT, 0);
    }
    gl.bindVertexArray(null);
    gl.disable(gl.BLEND);
  }
}
