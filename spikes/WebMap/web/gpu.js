// WebGL2 pipeline: height raster -> stylized field -> jump-flood SDF -> ink & parchment display.
// Every look decision downstream of the field (coast line, ripples, shading) is a function of
// one signed distance, which is also what the SVG exporter contours. One geometry, two renderers.

const VS = `#version 300 es
void main() {
  vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

const NOISE = `
const float PI = 3.141592653589793;
float hash3(vec3 p) {
  p = fract(p * 0.3183099 + vec3(0.71, 0.113, 0.419));
  p *= 17.0;
  return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float vnoise(vec3 x) {
  vec3 i = floor(x), f = fract(x);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(mix(hash3(i), hash3(i + vec3(1,0,0)), f.x),
                 mix(hash3(i + vec3(0,1,0)), hash3(i + vec3(1,1,0)), f.x), f.y),
             mix(mix(hash3(i + vec3(0,0,1)), hash3(i + vec3(1,0,1)), f.x),
                 mix(hash3(i + vec3(0,1,1)), hash3(i + vec3(1,1,1)), f.x), f.y), f.z) * 2.0 - 1.0;
}
float fbm(vec3 p, int oct) {
  float a = 0.5, s = 0.0;
  for (int i = 0; i < 8; i++) { if (i >= oct) break; s += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
  return s;
}
// Equirectangular uv (v = 0 at the north pole) <-> unit sphere (z up, matches Core's GeoCoord).
vec3 uvToSphere(vec2 uv) {
  float lon = (uv.x * 2.0 - 1.0) * PI, lat = (0.5 - uv.y) * PI;
  return vec3(cos(lat) * cos(lon), cos(lat) * sin(lon), sin(lat));
}
vec2 sphereToUv(vec3 p) {
  p = normalize(p);
  return vec2(atan(p.y, p.x) / (2.0 * PI) + 0.5, 0.5 - asin(clamp(p.z, -1.0, 1.0)) / PI);
}`;

// Field: the stylized height. Domain warp + fBm on the sphere (no seam at the antimeridian,
// no stretching at the poles). The sign of this field IS the coastline.
const FS_FIELD = `#version 300 es
precision highp float;
uniform sampler2D uHeight;
uniform sampler2D uBiome;
uniform vec2 uSize;
uniform float uWarp, uDetail, uSeed;
layout(location = 0) out vec4 oField;
layout(location = 1) out vec4 oColor;
${NOISE}
void main() {
  vec2 uv = gl_FragCoord.xy / uSize;
  vec3 p = uvToSphere(uv);
  vec3 o = vec3(uSeed * 0.137, uSeed * 0.071, uSeed * 0.029);
  vec3 q = p + uWarp * vec3(fbm(p * 6.0 + o, 5), fbm(p * 6.0 + o + 31.7, 5), fbm(p * 6.0 + o + 63.1, 5));
  vec2 wuv = sphereToUv(q);
  float h = texture(uHeight, wuv).r + uDetail * fbm(p * 48.0 + o, 6);
  oField = vec4(h, 0.0, 0.0, 1.0);
  oColor = vec4(texture(uBiome, wuv).rgb, 1.0);
}`;

// JFA seeds: sub-pixel zero-crossing estimates (Newton step along the gradient), so the
// distance field is not quantized to pixel centers.
const FS_SEED = `#version 300 es
precision highp float;
uniform sampler2D uField;
uniform ivec2 uSize;
out vec4 oSeed;
float F(ivec2 p) { p.x = (p.x + uSize.x) % uSize.x; p.y = clamp(p.y, 0, uSize.y - 1); return texelFetch(uField, p, 0).r; }
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy);
  float f = F(p);
  float l = F(p + ivec2(-1,0)), r = F(p + ivec2(1,0)), d = F(p + ivec2(0,-1)), u = F(p + ivec2(0,1));
  bool edge = (f >= 0.0) != (l >= 0.0) || (f >= 0.0) != (r >= 0.0) || (f >= 0.0) != (d >= 0.0) || (f >= 0.0) != (u >= 0.0);
  if (!edge) { oSeed = vec4(-1e9, -1e9, 0.0, 0.0); return; }
  vec2 g = vec2(r - l, u - d) * 0.5;
  vec2 off = -f * g / max(dot(g, g), 1e-12);
  if (length(off) > 1.0) off = normalize(off);
  oSeed = vec4(vec2(p) + 0.5 + off, 0.0, 0.0);
}`;

// One jump-flood round. Metric: x shrinks by cos(latitude), x wraps (it is a planet).
const FS_JFA = `#version 300 es
precision highp float;
uniform sampler2D uSeeds;
uniform ivec2 uSize;
uniform int uStep;
out vec4 oSeed;
float d2(vec2 a, vec2 s, float k) {
  float dx = abs(a.x - s.x); dx = min(dx, float(uSize.x) - dx) * k;
  float dy = a.y - s.y; return dx * dx + dy * dy;
}
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy);
  vec2 a = vec2(p) + 0.5;
  float k = max(cos((a.y / float(uSize.y) - 0.5) * 3.14159265), 0.02);
  vec2 best = vec2(-1e9); float bd = 1e30;
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    ivec2 q = p + ivec2(i, j) * uStep;
    if (q.y < 0 || q.y >= uSize.y) continue;
    q.x = (q.x % uSize.x + uSize.x) % uSize.x;
    vec2 s = texelFetch(uSeeds, q, 0).xy;
    if (s.x < -1e8) continue;
    float dd = d2(a, s, k);
    if (dd < bd) { bd = dd; best = s; }
  }
  oSeed = vec4(best, 0.0, 0.0);
}`;

const FS_SDF = `#version 300 es
precision highp float;
uniform sampler2D uSeeds, uField;
uniform ivec2 uSize;
out vec4 oSdf;
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy);
  vec2 a = vec2(p) + 0.5;
  vec2 s = texelFetch(uSeeds, p, 0).xy;
  float k = max(cos((a.y / float(uSize.y) - 0.5) * 3.14159265), 0.02);
  float dx = abs(a.x - s.x); dx = min(dx, float(uSize.x) - dx) * k;
  float d = s.x < -1e8 ? 1e4 : length(vec2(dx, a.y - s.y));
  oSdf = vec4(texelFetch(uField, p, 0).r >= 0.0 ? d : -d, 0.0, 0.0, 1.0);
}`;

// Display. d is in field pixels (+ land, - sea); everything is expressed in it.
const FS_SHOW = `#version 300 es
precision highp float;
uniform sampler2D uSdf, uColor, uField;
uniform vec2 uCanvas, uFieldSize, uCenter;
uniform float uZoom, uRing;
out vec4 oColor;
${NOISE}
float ink(float d, float w) { float aa = fwidth(d) * 0.75; return 1.0 - smoothstep(w - aa, w + aa, abs(d)); }
void main() {
  vec2 px = vec2(gl_FragCoord.x, uCanvas.y - gl_FragCoord.y);
  vec2 f = uCenter + (px - 0.5 * uCanvas) / uZoom;          // field pixels
  vec2 uv = f / uFieldSize;
  vec3 frame = vec3(0.22, 0.19, 0.16);
  if (uv.y < 0.0 || uv.y > 1.0) { oColor = vec4(frame, 1.0); return; }
  uv.x = fract(uv.x);
  float d = texture(uSdf, uv).r;
  vec3 p = uvToSphere(uv);
  float grain = fbm(p * 900.0, 3) * 0.5 + fbm(p * 70.0, 4);   // fixed to the map, not the screen

  vec3 seaInk = vec3(0.18, 0.27, 0.30), landInk = vec3(0.17, 0.13, 0.10);
  vec3 sea = vec3(0.74, 0.81, 0.79) + 0.02 * grain;
  vec3 col;
  if (d < 0.0) {
    float ds = -d;
    col = mix(sea, vec3(0.58, 0.70, 0.70), exp(-ds / 5.0) * 0.8);           // shallow halo
    // Ripple rings: offset copies of the coast, thinning and fading outward, broken by noise.
    float n = floor(ds / uRing + 0.5);
    float ringD = ds - n * uRing;
    float visible = step(0.5, n) * step(n, 4.5) * smoothstep(-0.25, 0.15, fbm(p * 140.0 + n * 3.1, 3) + 0.35 - n * 0.08);
    float w = max(0.55 / uZoom, 0.18 * (1.0 - n * 0.15));
    col = mix(col, seaInk, ink(ringD, w) * visible * (0.75 - n * 0.12));
  } else {
    vec3 land = texture(uColor, uv).rgb + 0.035 * grain;
    // Hill shade from the stylized field itself (NW light), kept subtle and warm.
    vec2 e = 1.0 / uFieldSize;
    float hx = texture(uField, uv + vec2(e.x, 0)).r - texture(uField, uv - vec2(e.x, 0)).r;
    float hy = texture(uField, uv + vec2(0, e.y)).r - texture(uField, uv - vec2(0, e.y)).r;
    float shade = clamp(-(hx - hy) * 40.0, -1.0, 1.0);
    land *= 1.0 + 0.18 * shade;
    land = mix(land, land * vec3(0.86, 0.80, 0.70), exp(-d / 3.0) * 0.6);    // inner coast tone
    col = land;
  }
  col = mix(col, landInk, ink(d, max(0.9 / uZoom, 0.32)));                  // the coastline
  col *= 1.0 - 0.06 * smoothstep(0.3, 1.0, fbm(p * 9.0, 3) + 0.5);         // paper stains
  oColor = vec4(col, 1.0);
}`;

export class MapGpu {
  constructor(canvas) {
    const gl = canvas.getContext('webgl2', { antialias: false, preserveDrawingBuffer: true });
    if (!gl) throw new Error('WebGL2 unavailable');
    if (!gl.getExtension('EXT_color_buffer_float')) throw new Error('EXT_color_buffer_float unavailable');
    this.gl = gl;
    this.canvas = canvas;
    this.vao = gl.createVertexArray();
    this.p = {
      field: this.program(FS_FIELD), seed: this.program(FS_SEED), jfa: this.program(FS_JFA),
      sdf: this.program(FS_SDF), show: this.program(FS_SHOW),
    };
    this.view = { cx: 0, cy: 0, zoom: 1 };
    this.ring = 6;
  }

  program(fs) {
    const gl = this.gl;
    const mk = (type, src) => {
      const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s);
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) + '\n' + src);
      return s;
    };
    const p = gl.createProgram();
    gl.attachShader(p, mk(gl.VERTEX_SHADER, VS)); gl.attachShader(p, mk(gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    const u = {};
    const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < n; i++) { const name = gl.getActiveUniform(p, i).name; u[name] = gl.getUniformLocation(p, name); }
    return { p, u };
  }

  tex(w, h, internal, format, type, filter, data = null) {
    const gl = this.gl, t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, internal, w, h, 0, format, type, data);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return t;
  }

  fbo(...textures) {
    const gl = this.gl, f = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, f);
    textures.forEach((t, i) => gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0 + i, gl.TEXTURE_2D, t, 0));
    gl.drawBuffers(textures.map((_, i) => gl.COLOR_ATTACHMENT0 + i));
    const st = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
    if (st !== gl.FRAMEBUFFER_COMPLETE) throw new Error('FBO incomplete ' + st);
    return f;
  }

  draw(prog, target, w, h, uniforms, textures = {}) {
    const gl = this.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, target);
    gl.viewport(0, 0, w, h);
    gl.useProgram(prog.p);
    let unit = 0;
    for (const [name, t] of Object.entries(textures)) {
      gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, t);
      gl.uniform1i(prog.u[name], unit++);
    }
    for (const [name, v] of Object.entries(uniforms)) {
      const loc = prog.u[name]; if (!loc) continue;
      if (typeof v === 'number') gl.uniform1f(loc, v);
      else if (v.int) (v.length === 2 ? gl.uniform2i(loc, v[0], v[1]) : gl.uniform1i(loc, v[0]));
      else gl.uniform2f(loc, v[0], v[1]);
    }
    gl.bindVertexArray(this.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  /** Builds field, SDF and color textures at fieldW x fieldH from the worker's raster. */
  build(raster, fieldW, fieldH, { seed, warp, detail }) {
    const gl = this.gl;
    this.dispose();
    const R = this.res = [];
    const keep = (t) => (R.push(t), t);
    const hTex = keep(this.tex(raster.width, raster.height, gl.R16F, gl.RED, gl.FLOAT, gl.LINEAR, raster.heights));
    const bTex = keep(this.tex(raster.width, raster.height, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, gl.LINEAR, raster.biomes));

    this.fieldTex = keep(this.tex(fieldW, fieldH, gl.R32F, gl.RED, gl.FLOAT, gl.NEAREST));
    this.colorTex = keep(this.tex(fieldW, fieldH, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, gl.LINEAR));
    this.fieldHalf = keep(this.tex(fieldW, fieldH, gl.R16F, gl.RED, gl.HALF_FLOAT, gl.LINEAR));
    const seedA = keep(this.tex(fieldW, fieldH, gl.RG32F, gl.RG, gl.FLOAT, gl.NEAREST));
    const seedB = keep(this.tex(fieldW, fieldH, gl.RG32F, gl.RG, gl.FLOAT, gl.NEAREST));
    this.sdfTex = keep(this.tex(fieldW, fieldH, gl.R32F, gl.RED, gl.FLOAT, gl.NEAREST));
    this.sdfHalf = keep(this.tex(fieldW, fieldH, gl.R16F, gl.RED, gl.HALF_FLOAT, gl.LINEAR));
    const fFieldColor = this.fbo(this.fieldTex, this.colorTex);
    const fA = this.fbo(seedA), fB = this.fbo(seedB), fSdf = this.fbo(this.sdfTex);
    const fFieldHalf = this.fbo(this.fieldHalf), fSdfHalf = this.fbo(this.sdfHalf);
    this.fbos = [fFieldColor, fA, fB, fSdf, fFieldHalf, fSdfHalf];

    const t0 = performance.now();
    const size = Object.assign([fieldW, fieldH], { int: true });
    this.draw(this.p.field, fFieldColor, fieldW, fieldH, { uSize: [fieldW, fieldH], uWarp: warp, uDetail: detail, uSeed: seed % 1000 }, { uHeight: hTex, uBiome: bTex });
    this.draw(this.p.seed, fA, fieldW, fieldH, { uSize: size }, { uField: this.fieldTex });
    let cur = { t: seedA, f: fA }, nxt = { t: seedB, f: fB }, passes = 0;
    for (let step = 1 << (Math.ceil(Math.log2(Math.max(fieldW, fieldH))) - 1); step >= 1; step >>= 1) {
      this.draw(this.p.jfa, nxt.f, fieldW, fieldH, { uSize: size, uStep: Object.assign([step], { int: true }) }, { uSeeds: cur.t });
      [cur, nxt] = [nxt, cur]; passes++;
    }
    this.draw(this.p.sdf, fSdf, fieldW, fieldH, { uSize: size }, { uSeeds: cur.t, uField: this.fieldTex });
    // Filterable half-float copies for display (R32F is not filterable without an extension).
    this.copyProgram ??= this.program(`#version 300 es
      precision highp float; uniform sampler2D uSrc; out vec4 o;
      void main() { o = texelFetch(uSrc, ivec2(gl_FragCoord.xy), 0); }`);
    this.draw(this.copyProgram, fSdfHalf, fieldW, fieldH, {}, { uSrc: this.sdfTex });
    this.draw(this.copyProgram, fFieldHalf, fieldW, fieldH, {}, { uSrc: this.fieldTex });
    const px = new Float32Array(4); gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.FLOAT, px); // sync for timing
    this.gpuMs = performance.now() - t0;
    this.jfaPasses = passes;
    this.fieldW = fieldW; this.fieldH = fieldH;
    this.view = { cx: fieldW / 2, cy: fieldH / 2, zoom: this.canvas.width / fieldW };
  }

  render() {
    const c = this.canvas;
    if (!this.sdfHalf) return;
    this.draw(this.p.show, null, c.width, c.height,
      { uCanvas: [c.width, c.height], uFieldSize: [this.fieldW, this.fieldH], uCenter: [this.view.cx, this.view.cy],
        uZoom: this.view.zoom, uRing: this.ring },
      { uSdf: this.sdfHalf, uColor: this.colorTex, uField: this.fieldHalf });
  }

  /** Signed distance (field px, + land) at full precision, row 0 = north. */
  readSdf() {
    const gl = this.gl, w = this.fieldW, h = this.fieldH;
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbos[3]);
    const fmt = gl.getParameter(gl.IMPLEMENTATION_COLOR_READ_FORMAT);
    const type = gl.getParameter(gl.IMPLEMENTATION_COLOR_READ_TYPE);
    if (fmt === gl.RED && type === gl.FLOAT) {
      const out = new Float32Array(w * h); gl.readPixels(0, 0, w, h, gl.RED, gl.FLOAT, out); return out;
    }
    const rgba = new Float32Array(w * h * 4); gl.readPixels(0, 0, w, h, gl.RGBA, gl.FLOAT, rgba);
    const out = new Float32Array(w * h); for (let i = 0; i < out.length; i++) out[i] = rgba[i * 4]; return out;
  }

  dispose() {
    const gl = this.gl;
    (this.res || []).forEach((t) => gl.deleteTexture(t));
    (this.fbos || []).forEach((f) => gl.deleteFramebuffer(f));
    this.res = []; this.fbos = []; this.sdfHalf = null;
  }
}
