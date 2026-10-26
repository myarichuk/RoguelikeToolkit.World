import { describe, expect, it } from 'vitest';
import { interiorCandidates, type Extent } from './RealmBorders';
import { CELL, GRID_H, GRID_W } from './RealmGrid';
import { overlaps } from './RealmLayer';

describe('realm name placement', () => {
  it('offers spaced spots deep inside the realm, deepest first', () => {
    const cells = new Int32Array(GRID_W * GRID_H).fill(-1);
    // A 40 x 20 block of realm 3.
    for (let y = 10; y < 30; y++) for (let x = 10; x < 50; x++) cells[y * GRID_W + x] = 3;
    const e: Extent = { id: 3, x0: 10, y0: 10, x1: 49, y1: 29, count: 800, sumX: 0, sumY: 0 };
    const spots = interiorCandidates(cells, e);
    expect(spots.length).toBeGreaterThan(1);
    for (const [x, y] of spots) {
      const cx = x / CELL - 0.5, cy = y / CELL - 0.5;
      expect(cells[cy * GRID_W + cx]).toBe(3); // inside the realm
    }
    const [fx, fy] = spots[0];
    expect(Math.abs(fy / CELL - 0.5 - 19.5)).toBeLessThanOrEqual(1); // the deepest row is the middle one
    expect(fx).toBeGreaterThan(0);
    for (let i = 0; i < spots.length; i++)
      for (let j = i + 1; j < spots.length; j++)
        expect(Math.abs(spots[i][0] - spots[j][0]) + Math.abs(spots[i][1] - spots[j][1])).toBeGreaterThanOrEqual(8 * CELL);
  });

  it('boxes overlap only when they share area', () => {
    expect(overlaps({ x0: 0, y0: 0, x1: 2, y1: 2 }, { x0: 1, y0: 1, x1: 3, y1: 3 })).toBe(true);
    expect(overlaps({ x0: 0, y0: 0, x1: 2, y1: 2 }, { x0: 2, y0: 0, x1: 3, y1: 2 })).toBe(false);
  });
});
