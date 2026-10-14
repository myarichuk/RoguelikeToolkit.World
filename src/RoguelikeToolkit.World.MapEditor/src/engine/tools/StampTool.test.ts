import { describe, expect, it } from 'vitest';
import { scatterDraw } from './StampTool';

describe('scatterDraw', () => {
  it('replays bit-identical dabs for the same stroke', () => {
    const a = scatterDraw(1337, 4, 100.5, 200.25, 6);
    expect(scatterDraw(1337, 4, 100.5, 200.25, 6)).toEqual(a);
  });

  it('stays inside the brush disc with a hand-like scale range', () => {
    for (let seq = 0; seq < 200; seq++) {
      const s = scatterDraw(1337, seq, 100, 200, 6);
      expect(Math.hypot(s.dx, s.dy)).toBeLessThanOrEqual(6);
      expect(s.scale).toBeGreaterThanOrEqual(0.85);
      expect(s.scale).toBeLessThan(1.15);
    }
  });

  it('a zero radius never offsets', () => {
    for (let seq = 0; seq < 20; seq++) {
      const s = scatterDraw(7, seq, 10, 10, 0);
      expect(s.dx).toBe(0);
      expect(s.dy).toBe(0);
    }
  });

  it('varies across dabs and seeds instead of repeating one stamp', () => {
    const seqs = new Set(Array.from({ length: 20 }, (_, seq) => JSON.stringify(scatterDraw(1337, seq, 100, 200, 6))));
    expect(seqs.size).toBeGreaterThan(15);
    expect(scatterDraw(1, 0, 100, 200, 6)).not.toEqual(scatterDraw(2, 0, 100, 200, 6));
  });
});
