// Content packs: everything that makes a map *look* a certain way is data, not code. A pack can
// ship themes (colors, line weights, label typography), biome definitions, stamp assets
// (procedural generator presets or authored SVG), placement rules for auto-population, and
// fonts. Packs layer: later packs override earlier ones by id. The built-in pack is just the
// first pack; it goes through the same loader and validator as a user's JSON file.
import type { PaletteToken } from '../procgen/ink';

export type { PaletteToken };

/** Look of the base map in the styled view. All widths are in screen pixels at zoom 1 unless noted. */
export interface MapStyle {
  paper: string;
  sea: string;
  shallow: string;
  seaInk: string;
  landInk: string;
  /** Coast line width in map units (scales with zoom, floored at ~1 screen px). */
  coastWidth: number;
  /** Shallow-water halo reach in map units. */
  haloWidth: number;
  rippleCount: number;
  rippleOpacity: number;
  /** 0..1 */
  hillshade: number;
  grain: number;
  stains: number;
  /** Fraction of the inner-coast darkening. */
  innerCoast: number;
  /** River water. */
  river: string;
  /** River width multiplier. */
  riverWidth: number;
  /** River bank ink (default: landInk). Thin rivers are drawn in it alone, as a pen line. */
  riverInk?: string;
  /** Bank ink width in map units (default 0.3). */
  riverInkWidth?: number;
  /** Road ink (default: landInk). */
  road?: string;
  /** Road width multiplier (default 1). */
  roadWidth?: number;
  frame: string;
}

export interface LabelStyle {
  /** Font key (see ContentPack.fonts). */
  font: string;
  /** Cap height in map units at weight 1. */
  size: number;
  /** Size grows with feature weight^sizeExp (0 = fixed). */
  sizeExp?: number;
  /** Extra letter spacing in em. */
  spacing: number;
  uppercase: boolean;
  fill: string;
  halo: string;
  /** Halo width in map units. */
  haloWidth: number;
  /** Labels below this zoom (screen px per map unit) are hidden; 0 = always. */
  minZoom?: number;
}

export interface Theme {
  id: string;
  name: string;
  /** Stamp ink colors by token. */
  palette: Record<PaletteToken, string>;
  map: MapStyle;
  /** Biome id -> land color (styled view). */
  biomes: Record<string, string>;
  /** Label kind -> typography. Kinds: capital, city, town, village, ruin, shrine, battlefield, river, lake, sea, ocean, mountain, valley, forest, region, title. */
  labels: Record<string, LabelStyle>;
  /** Optional UI chrome colors as CSS custom properties ("--bg": "#1d1a17"). */
  ui?: Record<string, string>;
}

export interface BiomeDef {
  id: number;
  name: string;
  /** Color in the analytic "biomes" view. */
  vivid: string;
}

export interface AssetDef {
  id: string;
  name: string;
  category: string;
  /** Visual height on the map in map units. */
  size: number;
  /** Procedural: generator id (procgen/stamps.ts) + parameters; `variants` seeds. */
  generator?: string;
  params?: Record<string, number>;
  variants?: number;
  /** Authored: inline SVG markup (one <svg> with a viewBox). Palette tokens may be used as colors: fill="token:ink". */
  svg?: string;
  /** Anchor in [0,1] of the SVG box (default bottom center). */
  anchor?: [number, number];
}

export interface PlacementRule {
  id: string;
  /** Candidate assets, picked uniformly. */
  assets: string[];
  /** Restrict to these biome ids (any if omitted). */
  biomes?: number[];
  /** Height window (sea level 0). */
  elevation?: [number, number];
  /** Grid spacing in map units (one candidate per cell). */
  spacing: number;
  /** Probability a candidate cell gets a stamp, 0..1. */
  density: number;
  /** Random scale range. */
  scale?: [number, number];
  /** Scale grows with height above this level (mountains). */
  scaleWithElevation?: number;
  /**
   * "grid" (default): one jittered candidate per cell. "relief": ranges and hill country. Candidates
   * are ranked by height and ridge prominence; the strongest are placed first and largest, each keeps
   * a footprint-sized gap, so a range reads as a chain of big peaks along its spine with smaller ones
   * on the flanks instead of an even carpet. Relief rules share one footprint map, in pack order.
   */
  layout?: 'grid' | 'relief';
  /** Relief: how much ridge prominence (height over the surroundings) counts against plain height, 0..1. Valleys are thinned in proportion. */
  ridge?: number;
  /** Relief: 0..1, roughly the share of the band left as open ground between clumps (hill country). */
  clump?: number;
  /** Relief: swap in other assets above a height ("peaks wear snow"), highest tier that fits wins. */
  tiers?: { above: number; assets: string[] }[];
}

export interface ContentPack {
  id: string;
  name: string;
  version: string;
  author?: string;
  description?: string;
  themes?: Theme[];
  biomes?: BiomeDef[];
  assets?: AssetDef[];
  rules?: PlacementRule[];
  /** Feature kind -> asset ids used for settlement/feature markers ("capital": ["core.capital"]). */
  featureAssets?: Record<string, string[]>;
  /** Font key -> URL (relative URLs resolve against the pack's own URL). */
  fonts?: Record<string, string>;
}
