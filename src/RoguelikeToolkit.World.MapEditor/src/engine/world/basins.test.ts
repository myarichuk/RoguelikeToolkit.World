import { describe, expect, it } from 'vitest';
import { oceanBasins, ringDistance, type Adjacency } from './features';

/** A W x H 4-connected grid as CSR adjacency (stands in for the icosphere: same contract). */
function grid(W: number, H: number): Adjacency {
  const start = new Int32Array(W * H + 1);
  const list: number[] = [];
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const t = y * W + x;
      if (x > 0) list.push(t - 1);
      if (x < W - 1) list.push(t + 1);
      if (y > 0) list.push(t - W);
      if (y < H - 1) list.push(t + W);
      start[t + 1] = list.length;
    }
  }
  return { start, list: Int32Array.from(list) };
}

const W = 100, H = 60;

function water(rects: [number, number, number, number][]): { tiles: number[]; adj: Adjacency; depth: Int32Array } {
  const adj = grid(W, H);
  const wet = new Uint8Array(W * H);
  for (const [x0, y0, x1, y1] of rects) for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) wet[y * W + x] = 1;
  const tiles: number[] = [];
  for (let t = 0; t < wet.length; t++) if (wet[t]) tiles.push(t);
  return { tiles, adj, depth: ringDistance(adj, (t) => wet[t] === 1, W * H) };
}

const connected = (tiles: number[], adj: Adjacency) => {
  const set = new Set(tiles), seen = new Set([tiles[0]]), q = [tiles[0]];
  for (let h = 0; h < q.length; h++) for (let k = adj.start[q[h]]; k < adj.start[q[h] + 1]; k++) {
    const u = adj.list[k];
    if (set.has(u) && !seen.has(u)) { seen.add(u); q.push(u); }
  }
  return seen.size === tiles.length;
};

describe('oceanBasins', () => {
  it('keeps one open sea whole, however big', () => {
    const { tiles, adj, depth } = water([[10, 5, 90, 55]]);
    const basins = oceanBasins(tiles, adj, depth, W * H);
    expect(basins).toHaveLength(1);
    expect(basins[0]).toHaveLength(tiles.length);
  });

  it('keeps a sea with bays and a ragged coast in one piece', () => {
    // A broad sea with narrow inlets on both sides: inlets belong to the sea, they are not seas.
    const { tiles, adj, depth } = water([[10, 15, 90, 45], [20, 5, 23, 15], [60, 45, 63, 56], [75, 5, 78, 15]]);
    const basins = oceanBasins(tiles, adj, depth, W * H);
    expect(basins).toHaveLength(1);
  });

  it('separates two basins joined only by a narrow strait, along the strait', () => {
    const left: [number, number, number, number] = [5, 10, 40, 50];
    const right: [number, number, number, number] = [60, 10, 95, 50];
    const { tiles, adj, depth } = water([left, right, [40, 28, 60, 31]]); // 3-wide, 20-long strait
    const basins = oceanBasins(tiles, adj, depth, W * H);
    expect(basins).toHaveLength(2);
    // Each basin is a single connected piece and together they cover the water exactly once.
    for (const b of basins) expect(connected(b, adj)).toBe(true);
    expect(basins.flat().sort((a, b) => a - b)).toEqual(tiles);
    // The strait is split between its two ends, not carved out as a third "sea".
    const inLeft = new Set(basins.find((b) => b.includes(20 * W + 20))!);
    expect(inLeft.has(29 * W + 42)).toBe(true);
    expect(inLeft.has(29 * W + 58)).toBe(false);
    expect(inLeft.has(20 * W + 80)).toBe(false);
  });

  it('is deterministic and independent of input tile order', () => {
    const { tiles, adj, depth } = water([[5, 10, 40, 50], [60, 10, 95, 50], [40, 28, 60, 31]]);
    const a = oceanBasins(tiles, adj, depth, W * H);
    const b = oceanBasins([...tiles].reverse(), adj, depth, W * H);
    expect(b).toEqual(a);
  });
});
