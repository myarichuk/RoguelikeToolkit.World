// Land use: what people (or fire) did to the ground, as opposed to what the climate grows on it.
//
// A biome is a climax community: temperature, rainfall, height and drainage decide it, and the physics
// reproduces it. Farmland is none of that. It sits on top of Plains, Forest or Savanna and does not
// change the climate; ashlands are a burn scar, not a climate zone. Keeping them in the biome raster
// destroyed the biome underneath and forced the physics to special-case their ids. They live in their
// own raster instead, and the renderer composites the two.

import type { Rect } from '../../core/geo';

export interface LandUseDef {
  id: number;
  name: string;
  /** Colour slot (the former biome id) the renderer uses, so existing themes and packs keep their colours. */
  slot: number;
  hint: string;
}

/**
 * Id 0 is "none". Ids are persisted in `.wmap`: never renumber. Ids 1..4 are Core's `LandUseId`, so a painted
 * stroke and a plugin's allocation mean the same thing; `slot` is only the editor's colour slot.
 */
export const LAND_USES: readonly LandUseDef[] = [
  { id: 1, name: 'Farmland', slot: 11, hint: 'Tilled fields. Drawn over whatever biome grows there; the biome underneath is unchanged.' },
  { id: 2, name: 'Ashlands', slot: 13, hint: 'Burn scars and volcanic waste: a consequence, not a use, so nothing is yielded. Drawn over the biome; the biome underneath is unchanged.' },
  { id: 3, name: 'Pasture', slot: 17, hint: 'Fenced or sown grazing land, as against a nomad\'s open range. Drawn over the biome; the biome underneath is unchanged.' },
  { id: 4, name: 'Built-up', slot: 18, hint: 'Ground under a town or city. Drawn over the biome; the biome underneath is unchanged.' },
];

/** Biome ids that used to hold land use (documents saved before the land-use layer). */
const LEGACY_SLOT_TO_USE = new Map(LAND_USES.map((u) => [u.slot, u.id] as const));
/** Colour slots that are land uses now, so the biome palette should not list them. */
export const LAND_USE_SLOTS: ReadonlySet<number> = new Set(LAND_USES.map((u) => u.slot));

export const landUseName = (id: number): string | undefined => LAND_USES.find((u) => u.id === id)?.name;

/** The renderer's colour slot for each land use id (index 0 unused). */
const SLOT_OF = new Uint8Array(256);
for (const u of LAND_USES) SLOT_OF[u.id] = u.slot;

/**
 * What the renderer draws: the land use's colour slot where one is painted on land, the biome elsewhere.
 * Sea stays sea (a stray stroke over water is stored but not drawn). Only rows y0..y1 are touched.
 */
export function composeBiomes(biome: Uint8Array, landuse: Uint8Array, out: Uint8Array, width: number, rect?: Rect): void {
  const height = biome.length / width;
  const y0 = rect ? Math.max(0, Math.floor(rect.y0)) : 0;
  const y1 = rect ? Math.min(height, Math.ceil(rect.y1)) : height;
  for (let i = y0 * width, end = y1 * width; i < end; i++) {
    const u = landuse[i];
    out[i] = u !== 0 && biome[i] !== 0 ? SLOT_OF[u] || biome[i] : biome[i];
  }
}

/**
 * Documents saved before the land-use layer painted farmland and ashlands as biomes 11 and 13. Moves
 * those pixels into `landuse` and gives the biome raster the nearest ordinary biome in their place
 * (multi-source flood over the four neighbours, row-major so the result is deterministic). Returns the
 * number of pixels moved; 0 means the file was already clean and nothing was touched.
 */
export function migrateLegacyLandUse(biome: Uint8Array, landuse: Uint8Array, width: number): number {
  const n = biome.length;
  let moved = 0;
  for (let i = 0; i < n; i++) {
    const u = LEGACY_SLOT_TO_USE.get(biome[i]);
    if (u !== undefined) { if (landuse[i] === 0) landuse[i] = u; moved++; }
  }
  if (moved === 0) return 0;

  const height = n / width;
  const legacy = (i: number) => LEGACY_SLOT_TO_USE.has(biome[i]);
  const queue = new Int32Array(n);
  const seen = new Uint8Array(n);
  let head = 0, tail = 0;
  for (let i = 0; i < n; i++) if (!legacy(i)) { seen[i] = 1; queue[tail++] = i; }
  if (tail === 0) { // the whole map was land use: plains is the only honest guess
    for (let i = 0; i < n; i++) biome[i] = 1;
    return moved;
  }
  const take = new Uint8Array(n); // the biome each legacy pixel adopts, kept apart so the fill order cannot leak into it
  while (head < tail) {
    const i = queue[head++], x = i % width, y = (i / width) | 0;
    const b = legacy(i) ? take[i] : biome[i];
    const visit = (j: number) => { if (!seen[j]) { seen[j] = 1; take[j] = b; queue[tail++] = j; } };
    visit(y * width + (x + 1) % width);
    visit(y * width + (x + width - 1) % width);
    if (y > 0) visit(i - width);
    if (y < height - 1) visit(i + width);
  }
  for (let i = 0; i < n; i++) if (legacy(i)) biome[i] = take[i];
  return moved;
}
