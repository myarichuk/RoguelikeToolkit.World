import { describe, expect, it } from 'vitest';
import type { NameRequest } from '../protocol';
import { uniqueNamesSync } from './uniqueNames';

const req = (seed: number): NameRequest => ({ kind: 'lake', seed, race: 'human', culture: 'c', context: 0 });
/** A namer with a tiny vocabulary, so collisions are common: the name depends only on the seed. */
const VOCAB = ['Elderglen', 'Lowglen', 'Langwaeter', 'Mistvale', 'Deepdale', 'Stonedale', 'Clearpool'];
const namer = (rs: NameRequest[]) => rs.map((r) => VOCAB[r.seed % VOCAB.length]);

describe('uniqueNames', () => {
  it('re-rolls names that collide within the batch', () => {
    const { names } = uniqueNamesSync([req(0), req(7), req(14), req(1)], namer);
    expect(new Set(names.map((n) => n.toLowerCase())).size).toBe(names.length);
    expect(names[0]).toBe('Elderglen');   // the first keeps its name
  });

  it('never takes a name already on the map, whatever its case', () => {
    const { names } = uniqueNamesSync([req(0)], namer, ['ELDERGLEN']);
    expect(names[0]).not.toBe('Elderglen');
  });

  it('returns the request that produced each name, and is deterministic', () => {
    const a = uniqueNamesSync([req(0), req(7)], namer), b = uniqueNamesSync([req(0), req(7)], namer);
    expect(a).toEqual(b);
    expect(namer([a.requests[1]])[0]).toBe(a.names[1]);
  });

  it('gives up after a few re-rolls instead of looping', () => {
    const { names } = uniqueNamesSync([req(0), req(1)], (rs) => rs.map(() => 'Same'));
    expect(names).toEqual(['Same', 'Same']);
  });
});
