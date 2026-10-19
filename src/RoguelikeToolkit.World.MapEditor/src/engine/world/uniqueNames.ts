import { hash2 } from '../core/rng';
import type { NameRequest } from './protocol';

/** Re-rolls a colliding name gets before it is kept as it is. */
const MAX_REROLLS = 4;

const key = (name: string) => name.trim().toLocaleLowerCase();

/** The request for a name's `attempt`-th re-roll: same place and culture, another seed. */
export const rerolled = (r: NameRequest, attempt: number): NameRequest => ({ ...r, seed: hash2(r.seed, 0x6e61 + attempt) & 0x7fffffff });

/**
 * Names a batch of places so that no two share a name, case-insensitively: not with each other
 * and not with `taken` (names already on the map, which always win). A name that collides is
 * re-rolled in its own culture with a derived seed, so the result stays deterministic per request.
 * A generator, so the same logic drives the worker's synchronous engine call and the editor's
 * asynchronous one: it yields a batch of requests and is resumed with their names.
 */
export function* uniqueNames(requests: NameRequest[], taken: Iterable<string> = []): Generator<NameRequest[], { names: string[]; requests: NameRequest[] }, string[]> {
  const used = new Set([...taken].map(key));
  const reqs = requests.slice();
  const names: string[] = new Array(reqs.length).fill('');
  let todo = reqs.map((_, i) => i);
  for (let attempt = 0; todo.length; attempt++) {
    if (attempt) for (const i of todo) reqs[i] = rerolled(requests[i], attempt);
    const got = yield todo.map((i) => reqs[i]);
    const again: number[] = [];
    todo.forEach((i, k) => {
      const name = got[k] ?? '';
      if (name && used.has(key(name)) && attempt < MAX_REROLLS) { again.push(i); return; }
      names[i] = name;
      if (name) used.add(key(name));
    });
    todo = again;
  }
  return { names, requests: reqs };
}

/** Runs `uniqueNames` against a synchronous namer. */
export function uniqueNamesSync(requests: NameRequest[], name: (r: NameRequest[]) => string[], taken?: Iterable<string>): { names: string[]; requests: NameRequest[] } {
  const g = uniqueNames(requests, taken);
  let step = g.next();
  while (!step.done) step = g.next(name(step.value));
  return step.value;
}

/** Runs `uniqueNames` against an asynchronous namer (the world worker). */
export async function uniqueNamesAsync(requests: NameRequest[], name: (r: NameRequest[]) => Promise<string[]>, taken?: Iterable<string>): Promise<{ names: string[]; requests: NameRequest[] }> {
  const g = uniqueNames(requests, taken);
  let step = g.next();
  while (!step.done) step = g.next(await name(step.value));
  return step.value;
}
