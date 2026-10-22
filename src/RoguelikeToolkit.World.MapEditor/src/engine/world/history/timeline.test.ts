import { describe, expect, it } from 'vitest';
import { existsAt, roadStateAt, since } from './timeline';

describe('timeline', () => {
  it('shows everything at the end of history, whatever the spans say', () => {
    expect(existsAt({ from: 30, until: 40 }, -1)).toBe(true);
  });

  it('shows a place from its first turn, and a ruin only once it has fallen', () => {
    expect([0, 4, 5, 20].map((t) => existsAt(since(5), t))).toEqual([false, false, true, true]);
  });

  it('hides a thing again after its span ends, and never hides one with no span', () => {
    expect([1, 2, 3].map((t) => existsAt({ from: 1, until: 3 }, t))).toEqual([true, true, false]);
    expect(existsAt(undefined, 0)).toBe(true);
  });

  it('draws a road only after it was built, and cut after a war severs it', () => {
    const road = { founded: 4, severed: 9 };
    expect([3, 4, 8, 9].map((t) => roadStateAt(road, t))).toEqual(['absent', 'open', 'open', 'cut']);
    expect(roadStateAt(road, -1)).toBe('cut');
    expect(roadStateAt({ severed: -1 }, 0)).toBe('open');
  });
});
