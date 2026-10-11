// Thin WebGL2 helpers. No abstraction beyond what removes boilerplate: programs with reflected
// uniform locations, textures, framebuffers, fullscreen draws.

export interface Program { p: WebGLProgram; u: Record<string, WebGLUniformLocation>; }

export const FULLSCREEN_VS = `#version 300 es
void main() {
  vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

export function compile(gl: WebGL2RenderingContext, vs: string, fs: string): Program {
  const mk = (type: number, src: string) => {
    const s = gl.createShader(type)!;
    gl.shaderSource(s, src); gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
      const log = gl.getShaderInfoLog(s);
      const numbered = src.split('\n').map((l, i) => `${i + 1}: ${l}`).join('\n');
      throw new Error(`${log}\n${numbered}`);
    }
    return s;
  };
  const p = gl.createProgram()!;
  gl.attachShader(p, mk(gl.VERTEX_SHADER, vs)); gl.attachShader(p, mk(gl.FRAGMENT_SHADER, fs));
  gl.linkProgram(p);
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) ?? 'link failed');
  const u: Record<string, WebGLUniformLocation> = {};
  const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS) as number;
  for (let i = 0; i < n; i++) {
    const name = gl.getActiveUniform(p, i)!.name.replace(/\[0\]$/, '');
    u[name] = gl.getUniformLocation(p, name)!;
  }
  return { p, u };
}

export interface TexSpec {
  w: number; h: number;
  internal: number; format: number; type: number;
  filter?: number;
  wrapX?: number;
  data?: ArrayBufferView | null;
}

export function texture(gl: WebGL2RenderingContext, s: TexSpec): WebGLTexture {
  const t = gl.createTexture()!;
  gl.bindTexture(gl.TEXTURE_2D, t);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, s.internal, s.w, s.h, 0, s.format, s.type, s.data ?? null);
  const f = s.filter ?? gl.NEAREST;
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, f);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, f === gl.LINEAR_MIPMAP_LINEAR ? gl.LINEAR : f);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, s.wrapX ?? gl.REPEAT);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  return t;
}

export function framebuffer(gl: WebGL2RenderingContext, ...textures: WebGLTexture[]): WebGLFramebuffer {
  const f = gl.createFramebuffer()!;
  gl.bindFramebuffer(gl.FRAMEBUFFER, f);
  textures.forEach((t, i) => gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0 + i, gl.TEXTURE_2D, t, 0));
  gl.drawBuffers(textures.map((_, i) => gl.COLOR_ATTACHMENT0 + i));
  const st = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
  if (st !== gl.FRAMEBUFFER_COMPLETE) throw new Error(`framebuffer incomplete: 0x${st.toString(16)}`);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  return f;
}

export type Uniform = number | boolean | [number, number] | [number, number, number] | [number, number, number, number] | { i: number | number[] } | Float32Array;

/** Binds textures (in order) and uniforms; numbers are floats unless wrapped as {i}. */
export function bind(gl: WebGL2RenderingContext, prog: Program, uniforms: Record<string, Uniform>, textures: Record<string, WebGLTexture>): void {
  gl.useProgram(prog.p);
  let unit = 0;
  for (const [name, t] of Object.entries(textures)) {
    const loc = prog.u[name];
    if (!loc) continue;
    gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, t);
    gl.uniform1i(loc, unit++);
  }
  for (const [name, v] of Object.entries(uniforms)) {
    const loc = prog.u[name];
    if (!loc) continue;
    if (typeof v === 'number') gl.uniform1f(loc, v);
    else if (typeof v === 'boolean') gl.uniform1i(loc, v ? 1 : 0);
    else if (v instanceof Float32Array) gl.uniform3fv(loc, v);
    else if ('i' in v) {
      const a = Array.isArray(v.i) ? v.i : [v.i];
      if (a.length === 1) gl.uniform1i(loc, a[0]); else if (a.length === 2) gl.uniform2i(loc, a[0], a[1]); else gl.uniform4i(loc, a[0], a[1], a[2], a[3]);
    } else if (v.length === 2) gl.uniform2f(loc, v[0], v[1]);
    else if (v.length === 3) gl.uniform3f(loc, v[0], v[1], v[2]);
    else gl.uniform4f(loc, v[0], v[1], v[2], v[3]);
  }
}

/** "#rrggbb" -> [r, g, b] in 0..1. */
export function rgb(hex: string): [number, number, number] {
  const h = hex.replace('#', '');
  const v = h.length === 3 ? h.split('').map((c) => c + c).join('') : h.slice(0, 6);
  const n = parseInt(v, 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}
