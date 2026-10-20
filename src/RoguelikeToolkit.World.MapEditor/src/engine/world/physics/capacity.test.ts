import { describe, expect, it } from 'vitest';
import { CAPACITY_KINDS, CAPACITY_STRIDE, capacityOf } from './capacity';

describe('capacityOf', () => {
  const cap = Uint8Array.from([10, 20, 30, 40, 50, 11, 21, 31, 41, 51]);
  it('reads each product from the interleaved array, in Core\'s field order', () => {
    expect(CAPACITY_KINDS.map((k) => capacityOf(cap, 0, k.key))).toEqual([10, 20, 30, 40, 50]);
    expect(CAPACITY_KINDS.map((k) => capacityOf(cap, 1, k.key))).toEqual([11, 21, 31, 41, 51]);
    expect(CAPACITY_KINDS.length).toBe(CAPACITY_STRIDE);
  });
  it('is 0 without a cache, off-world or past the end', () => {
    expect(capacityOf(undefined, 0, 'arable')).toBe(0);
    expect(capacityOf(cap, -1, 'arable')).toBe(0);
    expect(capacityOf(cap, 2, 'fish')).toBe(0);
  });
  it('gives every kind a hint, because the controls are tooltipped', () => {
    for (const k of CAPACITY_KINDS) expect(k.hint.length).toBeGreaterThan(20);
  });
});
