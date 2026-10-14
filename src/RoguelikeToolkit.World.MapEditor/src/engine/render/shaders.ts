// GLSL. Conventions:
//  - "mu" = map units (core/geo.ts). Texture row 0 is the north edge; gl_FragCoord.y = 0 is row 0.
//  - All stylistic noise is sampled on a cylinder wrapped around the map (x periodic, isotropic in
//    mu), never on the sphere: equal map distance = equal visual frequency, at any latitude.
//  - Distances (SDF) are Euclidean in mu: a ripple ring is equally wide at 0° and 75°.

export const NOISE = `
const float PI = 3.141592653589793;
uniform vec2 uMap;            // MAP_W, MAP_H (mu)
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
// Map point -> cylinder of circumference MAP_W (mu units throughout): seamless in x, isotropic.
vec3 cyl(vec2 mu) {
  float a = mu.x / uMap.x * 2.0 * PI, r = uMap.x / (2.0 * PI);
  return vec3(r * cos(a), r * sin(a), mu.y);
}
// Domain warp shared by every pass that must agree on where the coast is.
uniform float uWarp, uSeed;
vec2 warp(vec2 mu) {
  vec3 c = cyl(mu) / 54.0 + vec3(uSeed * 0.137, uSeed * 0.071, uSeed * 0.029);
  return mu + uWarp * vec2(fbm(c, 5), fbm(c + 31.7, 5));
}
vec2 muToUv(vec2 mu) { return vec2(mu.x / uMap.x, clamp(mu.y / uMap.y, 0.0, 1.0)); }
`;

/** Stylized height at field resolution: elevation sampled through the warp, plus detail noise. */
export const FS_FIELD = `#version 300 es
precision highp float;
uniform sampler2D uElev;
uniform float uScale, uDetail;
out vec4 oField;
${NOISE}
void main() {
  vec2 mu = gl_FragCoord.xy / uScale;
  vec2 q = warp(mu);
  float h = texture(uElev, muToUv(q)).r;
  h += uDetail * fbm(cyl(mu) / 6.8 + vec3(uSeed * 0.31), 6);
  oField = vec4(h, 0.0, 0.0, 1.0);
}`;

/** Land colors at 1 mu/texel: biome ids through the same warp, soft-blended by two noise-rotated tap rings averaged in linear space (an sRGB average darkens the mids into a muddy fringe). */
export const FS_COLORIZE = `#version 300 es
precision highp float;
precision highp usampler2D;
uniform usampler2D uBiome;
uniform sampler2D uPalette;   // 256 x 1 RGBA
out vec4 oColor;
${NOISE}
vec3 pal(vec2 mu) {
  ivec2 s = textureSize(uBiome, 0);
  ivec2 t = ivec2(int(floor(mod(mu.x, uMap.x))), clamp(int(floor(mu.y)), 0, s.y - 1));
  uint id = texelFetch(uBiome, t, 0).r;
  return texelFetch(uPalette, ivec2(int(id), 0), 0).rgb;
}
void main() {
  vec2 mu = gl_FragCoord.xy;
  vec2 q = warp(mu);
  float a = hash3(vec3(mu, 7.0)) * 6.2831853;
  vec3 acc = pal(q) * pal(q) * 2.0;
  for (int i = 0; i < 6; i++) {
    float t = a + float(i) * 1.0471976;
    vec2 o = vec2(cos(t), sin(t));
    vec3 c0 = pal(q + o * 0.95);
    vec3 c1 = pal(q - o * 2.1);
    acc += c0 * c0 + 0.5 * c1 * c1;
  }
  oColor = vec4(sqrt(acc / 11.0), 1.0);
}`;

/**
 * JFA seeds. Seeds are stored as offsets from the texel center (RG16F): sub-pixel zero crossings
 * found with one Newton step along the field gradient. Offsets stay small (bounded JFA), so half
 * floats keep ~1/16 px precision at a fraction of the memory of absolute RG32F coordinates.
 */
export const FS_SEED = `#version 300 es
precision highp float;
uniform sampler2D uField;
uniform ivec2 uSize;
out vec4 oSeed;
float F(ivec2 p) { p.x = (p.x + uSize.x) % uSize.x; p.y = clamp(p.y, 0, uSize.y - 1); return texelFetch(uField, p, 0).r; }
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy);
  float f = F(p);
  float l = F(p + ivec2(-1,0)), r = F(p + ivec2(1,0)), d = F(p + ivec2(0,-1)), u = F(p + ivec2(0,1));
  bool s = f >= 0.0;
  bool edge = s != (l >= 0.0) || s != (r >= 0.0) || s != (d >= 0.0) || s != (u >= 0.0);
  if (!edge) { oSeed = vec4(1e4, 1e4, 0.0, 0.0); return; }
  vec2 g = vec2(r - l, u - d) * 0.5;
  vec2 off = -f * g / max(dot(g, g), 1e-12);
  if (length(off) > 1.0) off = normalize(off);
  oSeed = vec4(off, 0.0, 0.0);
}`;

/**
 * One jump-flood round over a region. Reads outside uRect count as "no seed", which makes an
 * incremental update a self-contained JFA over the dirty region (see TerrainPipeline). With
 * uWrap the region spans the full width and x wraps.
 */
export const FS_JFA = `#version 300 es
precision highp float;
uniform sampler2D uSeeds;
uniform ivec2 uSize;
uniform int uStep;
uniform ivec4 uRect;   // x0, y0, x1, y1 (exclusive)
uniform bool uWrap;
out vec4 oSeed;
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy);
  vec2 best = vec2(1e4); float bd = 1e30;
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    ivec2 d = ivec2(i, j) * uStep;
    ivec2 q = p + d;
    if (q.y < uRect.y || q.y >= uRect.w) continue;
    if (uWrap) q.x = (q.x % uSize.x + uSize.x) % uSize.x;
    else if (q.x < uRect.x || q.x >= uRect.z) continue;
    vec2 o = texelFetch(uSeeds, q, 0).xy;
    if (o.x > 9000.0) continue;
    vec2 cand = vec2(d) + o;
    float dd = dot(cand, cand);
    if (dd < bd) { bd = dd; best = cand; }
  }
  oSeed = vec4(best, 0.0, 0.0);
}`;

/** Signed distance in mu (+ land). Unreached texels saturate at uMaxDist. */
export const FS_SDF = `#version 300 es
precision highp float;
uniform sampler2D uSeeds, uField;
uniform float uScale, uMaxDist;
out vec4 oSdf;
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy);
  vec2 o = texelFetch(uSeeds, p, 0).xy;
  float d = o.x > 9000.0 ? uMaxDist : min(length(o) / uScale, uMaxDist);
  oSdf = vec4(texelFetch(uField, p, 0).r >= 0.0 ? d : -d, 0.0, 0.0, 1.0);
}`;

/** Per-frame map display. One shader, view modes and overlays as uniforms. */
export const FS_DISPLAY = `#version 300 es
precision highp float;
precision highp usampler2D;
uniform sampler2D uSdf, uField, uColor, uElev, uVivid, uMask;
uniform usampler2D uBiome, uPlate;
uniform vec2 uCanvas, uCenter, uFieldSize;
uniform float uZoom;
uniform int uMode;             // 0 styled, 1 biomes, 2 elevation, 3 plates
uniform int uFlags;            // 1 hillshade, 2 contours, 4 plate boundaries, 8 graticule, 16 ripples, 32 coast, 64 selection
uniform float uPlateTopo;      // plates view: 0 flat colors .. 1 over topography
// Theme
uniform vec3 uPaper, uSea, uShallow, uSeaInk, uLandInk, uFrame, uAccent;
uniform float uCoastW, uHalo, uRing, uRings, uRippleOpacity, uShade, uGrain, uStains, uInnerCoast;
out vec4 oColor;
${NOISE}
float lineMask(float d, float w) { float aa = fwidth(d) * 0.75 + 1e-5; return 1.0 - smoothstep(w - aa, w + aa, abs(d)); }
float px() { return 1.0 / uZoom; }   // one device pixel in mu

vec3 hypsometric(float h) {
  if (h < 0.0) return mix(vec3(0.62, 0.78, 0.86), vec3(0.10, 0.25, 0.45), clamp(-h / 0.8, 0.0, 1.0));
  vec3 c0 = vec3(0.42, 0.62, 0.36), c1 = vec3(0.85, 0.80, 0.50), c2 = vec3(0.60, 0.45, 0.32), c3 = vec3(0.97, 0.97, 0.97);
  return h < 0.3 ? mix(c0, c1, h / 0.3) : h < 0.65 ? mix(c1, c2, (h - 0.3) / 0.35) : mix(c2, c3, clamp((h - 0.65) / 0.3, 0.0, 1.0));
}
vec3 plateColor(uint id) {
  float h = hash3(vec3(float(id) * 1.37, 3.1, 7.7));
  vec3 c = 0.55 + 0.45 * cos(6.2831853 * (h + vec3(0.0, 0.33, 0.67)));
  return mix(c, vec3(0.9), 0.15);
}
float shadeOf(sampler2D tex, vec2 uv, vec2 texel, float k) {
  float hx = texture(tex, uv + vec2(texel.x, 0)).r - texture(tex, uv - vec2(texel.x, 0)).r;
  float hy = texture(tex, uv + vec2(0, texel.y)).r - texture(tex, uv - vec2(0, texel.y)).r;
  return clamp(-(hx - hy) * k, -1.0, 1.0);
}
// Plates are whole planet tiles, so a raw lookup traces hex edges. Sample them where the coast and biome colours are
// sampled (the shared warp), plus a finer jitter, so a border wanders like the rest of the map instead of stair-stepping.
uint plateAt(vec2 q) {
  ivec2 s = textureSize(uPlate, 0);
  return texelFetch(uPlate, ivec2(int(floor(mod(q.x, uMap.x))), clamp(int(floor(q.y)), 0, s.y - 1)), 0).r;
}
vec2 plateWarp(vec2 mu) {
  vec3 c = cyl(mu) / 15.0 + vec3(uSeed * 0.053);
  return warp(mu) + 8.0 * vec2(fbm(c, 4), fbm(c + 11.3, 4));
}
float latDeg(float y) { return degrees(2.0 * atan(exp((0.5 - y / uMap.y) * 2.0 * PI * uMap.y / uMap.x)) - PI / 2.0); }

void main() {
  vec2 sp = vec2(gl_FragCoord.x, uCanvas.y - gl_FragCoord.y);
  vec2 mu = uCenter + (sp - 0.5 * uCanvas) / uZoom;
  if (mu.y < 0.0 || mu.y > uMap.y) { oColor = vec4(uFrame, 1.0); return; }
  mu.x = mod(mu.x, uMap.x);
  vec2 uv = mu / uMap;
  float d = texture(uSdf, uv).r;           // mu, + land
  vec3 c = cyl(mu);
  float grain = fbm(c / 0.36, 3) * 0.5 + fbm(c / 4.7, 4);
  ivec2 tx = ivec2(int(mu.x), int(mu.y));
  vec3 col;

  if (uMode == 0) {
    if (d < 0.0) {
      float ds = -d;
      col = mix(uSea + 0.02 * grain, uShallow, exp(-ds / uHalo) * 0.8);
      if ((uFlags & 16) != 0) {
        float n = floor(ds / uRing + 0.5);
        float ringD = ds - n * uRing;
        float broken = smoothstep(-0.25, 0.15, fbm(c / 2.3 + n * 3.1, 3) + 0.35 - n * 0.08);
        float visible = step(0.5, n) * step(n, uRings + 0.5) * broken;
        float w = max(0.55 * px(), uRing * 0.07 * (1.0 - n * 0.12));
        col = mix(col, uSeaInk, lineMask(ringD, w) * visible * uRippleOpacity * (1.0 - n / (uRings + 1.5)));
      }
    } else {
      vec3 land = texture(uColor, uv).rgb + uGrain * grain;
      // Relief reads across biomes: high ground pales toward the paper, so a hill in
      // plains and a hill in forest share a height cue no flat biome wash carries.
      float hTopo = texture(uElev, uv).r;
      land = mix(land, uPaper + uGrain * grain, smoothstep(0.40, 0.85, hTopo) * 0.45);
      land *= 1.0 + uShade * shadeOf(uField, uv, 1.0 / uFieldSize, 40.0);
      land = mix(land, land * vec3(0.86, 0.80, 0.70), exp(-d / 1.6) * uInnerCoast);
      col = land;
    }
    col = mix(col, uLandInk, lineMask(d, max(0.9 * px(), uCoastW)));
    col *= 1.0 - uStains * smoothstep(0.3, 1.0, fbm(c / 48.0, 3) + 0.5);
  } else {
    float h = texture(uElev, uv).r;
    if (uMode == 1) {
      uint b = texelFetch(uBiome, tx, 0).r;
      col = d < 0.0 ? mix(vec3(0.30, 0.52, 0.72), vec3(0.14, 0.30, 0.52), clamp(-h / 0.6, 0.0, 1.0))
                    : texelFetch(uVivid, ivec2(int(b), 0), 0).rgb;
    } else if (uMode == 2) {
      col = hypsometric(h);
    } else {
      uint pl = plateAt(plateWarp(mu));
      vec3 pc = plateColor(pl & 0xffffu);
      col = mix(pc, pc * (0.55 + 0.6 * hypsometric(h)), uPlateTopo);
      if ((pl >> 24 & 1u) == 1u) col *= 0.9;   // oceanic crust a touch darker
    }
    if ((uFlags & 1) != 0) col *= 1.0 + 0.35 * shadeOf(uElev, uv, 1.0 / uMap, 18.0);
    if ((uFlags & 32) != 0) col = mix(col, vec3(0.1), lineMask(d, max(0.8 * px(), 0.2)) * 0.8);
  }

  if ((uFlags & 2) != 0) {   // contours every 0.1 height units, index line every 0.5
    float h = texture(uElev, uv).r;
    float k = h / 0.1, fk = abs(fract(k + 0.5) - 0.5) * 0.1;
    float idx = abs(fract(h / 0.5 + 0.5) - 0.5) < 0.1 ? 1.0 : 0.0;
    col = mix(col, vec3(0.25, 0.18, 0.12), lineMask(fk, 0.6 * fwidth(h) + 1e-5) * (0.35 + 0.35 * idx));
  }
  if ((uFlags & 4) != 0) {   // plate boundaries, colored by boundary type
    vec2 pq = plateWarp(mu);   // the warp is smooth, so the taps below ride the same displacement
    uint me = plateAt(pq);
    float r = max(1.0, 1.2 * px());
    bool edge = false;
    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
      if ((plateAt(pq + vec2(float(i), float(j)) * r) & 0xffffu) != (me & 0xffffu)) edge = true;
    }
    if (edge) {
      uint bt = (me >> 16) & 0xffu;
      vec3 bc = bt == 1u ? vec3(0.80, 0.18, 0.15) : bt == 2u ? vec3(0.15, 0.45, 0.85) : bt == 3u ? vec3(0.20, 0.65, 0.30) : vec3(0.25);
      col = mix(col, bc, 0.9);
    }
  }
  if ((uFlags & 8) != 0) {   // graticule every 15°
    float lon = (mu.x / uMap.x - 0.5) * 360.0, lat = latDeg(mu.y);
    float gl = abs(fract(lon / 15.0 + 0.5) - 0.5) * 15.0, gt = abs(fract(lat / 15.0 + 0.5) - 0.5) * 15.0;
    float g = max(lineMask(gl, fwidth(lon) * 0.6), lineMask(gt, fwidth(lat) * 0.6));
    col = mix(col, uSeaInk, g * 0.35);
  }
  if ((uFlags & 64) != 0) {  // selection (r) and hover (g) masks
    vec2 m = texture(uMask, uv).rg;
    float edgeS = lineMask(m.r - 0.5, fwidth(m.r) * 1.5), edgeH = lineMask(m.g - 0.5, fwidth(m.g) * 1.2);
    col = mix(col, uAccent, m.r * 0.18);
    col = mix(col, uAccent, edgeS * 0.95);
    col = mix(col, uAccent, edgeH * 0.6 + m.g * 0.06);
  }
  oColor = vec4(col, 1.0);
}`;

/** Instanced stamps from an atlas. Per instance: pos.xy (mu), size.xy (mu), anchor.xy, rot, flip; uv rect. */
export const VS_STAMP = `#version 300 es
layout(location = 0) in vec2 aCorner;      // 0..1 quad
layout(location = 1) in vec4 iPosSize;     // x, y, w, h (mu)
layout(location = 2) in vec4 iAnchorRot;   // ax, ay, rotation, flip(+-1)
layout(location = 3) in vec4 iUv;          // u0, v0, u1, v1
layout(location = 4) in float iAlpha;
uniform vec2 uCanvas, uCenter;
uniform float uZoom, uOffset;
out vec2 vUv;
out float vAlpha;
void main() {
  vec2 local = (aCorner - iAnchorRot.xy) * iPosSize.zw;
  local.x *= iAnchorRot.w;
  float c = cos(iAnchorRot.z), s = sin(iAnchorRot.z);
  vec2 mu = iPosSize.xy + vec2(uOffset, 0.0) + vec2(c * local.x - s * local.y, s * local.x + c * local.y);
  vec2 sp = (mu - uCenter) * uZoom + 0.5 * uCanvas;
  gl_Position = vec4(sp.x / uCanvas.x * 2.0 - 1.0, 1.0 - sp.y / uCanvas.y * 2.0, 0.0, 1.0);
  vUv = mix(iUv.xy, iUv.zw, aCorner);
  vAlpha = iAlpha;
}`;

export const FS_STAMP = `#version 300 es
precision highp float;
uniform sampler2D uAtlas;
in vec2 vUv;
in float vAlpha;
out vec4 oColor;
void main() {
  vec4 c = texture(uAtlas, vUv);
  oColor = vec4(c.rgb, c.a * vAlpha);   // atlas is premultiplied
  if (oColor.a < 0.002) discard;
}`;

/**
 * River ribbons, expanded in the vertex shader so a river never gets thinner than ~1.5 device
 * px at any zoom (it fades instead), while wide rivers keep their map-unit width.
 */
export const VS_RIVER = `#version 300 es
layout(location = 0) in vec2 aPos;      // centerline point (mu)
layout(location = 1) in vec2 aNormal;   // unit (miter) normal
layout(location = 2) in float aHalfW;   // half width (mu)
layout(location = 3) in float aSide;    // -1 | +1
uniform vec2 uCanvas, uCenter;
uniform float uZoom, uOffset, uWidthScale;
out float vEdge;
out float vHalfPx;
void main() {
  float want = aHalfW * uWidthScale * uZoom;
  float hp = max(want, 0.9);
  vec2 mu = aPos + vec2(uOffset, 0.0) + aNormal * aSide * (hp / uZoom);
  vec2 sp = (mu - uCenter) * uZoom + 0.5 * uCanvas;
  gl_Position = vec4(sp.x / uCanvas.x * 2.0 - 1.0, 1.0 - sp.y / uCanvas.y * 2.0, 0.0, 1.0);
  vEdge = aSide;
  vHalfPx = hp * clamp(want / 0.9, 0.3, 1.0);   // < 1: alpha fade for sub-pixel rivers
}`;

export const FS_RIVER = `#version 300 es
precision highp float;
uniform vec3 uColor;
in float vEdge;
in float vHalfPx;
out vec4 oColor;
void main() {
  float hp = max(vHalfPx, 0.9);
  float a = 1.0 - smoothstep(1.0 - 1.0 / hp, 1.0, abs(vEdge));
  a *= clamp(vHalfPx / 0.9, 0.3, 1.0);
  oColor = vec4(uColor * a, a);
}`;
