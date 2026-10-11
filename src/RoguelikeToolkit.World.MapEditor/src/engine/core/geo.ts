// Map space: a Mercator projection in "map units" (mu). x in [0, MAP_W) spans 360° and wraps
// (it is a planet); y in [0, MAP_H) runs north -> south and is clipped at ±MAX_LAT.
//
// Why Mercator: it is conformal, so shapes stay locally undistorted, and everything stylistic
// (coast SDF, ripple rings, noise grain, stamp and label sizes) is computed in this flat space
// with a plain Euclidean metric. A ripple ring is then equally wide at the equator and at 75°N.
// The sphere is only the data source (heights, biomes, plates); it never leaks into styling.
// The projection is isolated here, so another one (a regional azimuthal) can replace it later.
export const MAP_W = 2048;
export const MAP_H = 1536;

/** Half the map height in Mercator radians: y_merc = ln(tan(pi/4 + lat/2)). */
export const MERC_HALF = (MAP_H / 2) * (2 * Math.PI) / MAP_W; // = 3*pi/4 for 2048x1536
/** Latitude cut, ~79.17°. */
export const MAX_LAT = 2 * Math.atan(Math.exp(MERC_HALF)) - Math.PI / 2;

export interface Vec2 { x: number; y: number; }

/** Wraps x into [0, MAP_W). */
export const wrapX = (x: number): number => ((x % MAP_W) + MAP_W) % MAP_W;

/** Shortest signed x distance a -> b on the wrapped map. */
export function dxWrap(a: number, b: number): number {
  let d = b - a;
  if (d > MAP_W / 2) d -= MAP_W; else if (d < -MAP_W / 2) d += MAP_W;
  return d;
}

/** Radians -> map units. Latitudes beyond the cut clamp to the map edge. */
export function lonLatToMap(lon: number, lat: number): Vec2 {
  const l = Math.max(-MAX_LAT, Math.min(MAX_LAT, lat));
  const m = Math.log(Math.tan(Math.PI / 4 + l / 2));
  return { x: (lon / (2 * Math.PI) + 0.5) * MAP_W, y: (0.5 - m / (2 * MERC_HALF)) * MAP_H };
}

/** Latitude (radians) of a map row coordinate. */
export const latOfY = (y: number): number =>
  2 * Math.atan(Math.exp((0.5 - y / MAP_H) * 2 * MERC_HALF)) - Math.PI / 2;

/** Longitude (radians) of a map column coordinate. */
export const lonOfX = (x: number): number => (wrapX(x) / MAP_W - 0.5) * 2 * Math.PI;

/** Unit vector (z up, Core's convention) -> map units. */
export function vecToMap(x: number, y: number, z: number): Vec2 {
  return lonLatToMap(Math.atan2(y, x), Math.asin(Math.max(-1, Math.min(1, z))));
}

export function mapToLonLatDeg(x: number, y: number): { lon: number; lat: number } {
  return { lon: lonOfX(x) * 180 / Math.PI, lat: latOfY(y) * 180 / Math.PI };
}

/** Kilometres per map unit at row y (Mercator scale grows as 1/cos(lat)). */
export const kmPerMu = (y: number): number => (2 * Math.PI * 6371 / MAP_W) * Math.cos(latOfY(y));

export interface Rect { x0: number; y0: number; x1: number; y1: number; }

export const rectUnion = (a: Rect | null, b: Rect): Rect =>
  a ? { x0: Math.min(a.x0, b.x0), y0: Math.min(a.y0, b.y0), x1: Math.max(a.x1, b.x1), y1: Math.max(a.y1, b.y1) } : { ...b };

export const clamp = (v: number, lo: number, hi: number): number => (v < lo ? lo : v > hi ? hi : v);
