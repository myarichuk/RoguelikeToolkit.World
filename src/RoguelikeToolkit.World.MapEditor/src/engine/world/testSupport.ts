import type { PackedEdits, TileEdit } from './edits/edits';

/** Inverse of packEdits, for tests that play the engine. */
export function unpackForTest(p: PackedEdits): Map<number, TileEdit> {
  const m = new Map<number, TileEdit>();
  for (let k = 0; k < p.tiles.length; k++) {
    const e: TileEdit = {};
    if (p.delta[k] !== 0) e.dh = p.delta[k];
    if (p.meta[k] & 1) e.pinH = p.pinHeight[k];
    if (p.meta[k] & 2) e.pinBiome = (p.meta[k] >> 8) & 0xff;
    if (p.meta[k] & 4) e.lu = (p.meta[k] >> 16) & 0xff;
    if (p.temperature[k] !== 0) e.dt = p.temperature[k];
    if (p.precipitation[k] !== 0) e.dr = p.precipitation[k];
    m.set(p.tiles[k], e);
  }
  return m;
}
