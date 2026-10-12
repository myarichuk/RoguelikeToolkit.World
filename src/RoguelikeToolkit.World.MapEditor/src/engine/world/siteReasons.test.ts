import { describe, expect, it } from 'vitest';
import { siteReasons } from './features';

describe('siteReasons', () => {
  it('turns the engine terms into plain words, in order', () => {
    expect(siteReasons('water,fertile')).toBe('fresh water, fertile land');
    expect(siteReasons('winter-ice,high,past-events')).toBe('sea ice in winter, high ground, past events');
  });
  it('is empty for a site with no terms, or no entry at all', () => {
    expect(siteReasons('')).toBe('');
    expect(siteReasons(undefined)).toBe('');
  });
  it('passes a term it does not know through rather than dropping it', () => {
    expect(siteReasons('water,future-term')).toBe('fresh water, future-term');
  });
});
