import { hash2 } from '../core/rng';
import type { NameStyle } from './protocol';

export interface NamingInput {
  seed: number;
  n: number;
  /** CSR adjacency: neighbours of t are list[start[t] .. start[t + 1]). */
  adj: { start: Int32Array; list: Int32Array };
  isLand: (t: number) => boolean;
  /** Owner polity id per tile (-1 unclaimed); empty when the world has no history. */
  owner: ArrayLike<number>;
  /** The name style each polity id renders through (an index into `styles`). */
  polityStyle: ReadonlyMap<number, number>;
  styleCount: number;
}

/**
 * The name style (index into the chosen styles) that names places on each tile: the owning realm's own style where history
 * claims the tile; otherwise the nearest claimed tile's, spreading over land first and then out over the water. Land no realm
 * reaches (and every tile of a world without history) takes one style per landmass, so a continent still sounds like itself.
 * Shared by feature naming and the editor's `nameAt`, so a hand-placed label sounds like the town next to it.
 */
export function tileNamingStyles(c: NamingInput): Uint8Array {
  const { n, adj } = c;
  const out = new Uint8Array(n);
  if (!c.styleCount) return out;
  const NONE = 255;
  out.fill(NONE);
  const queue = new Int32Array(n);
  let head = 0, tail = 0;
  const spread = (landOnly: boolean) => {
    while (head < tail) {
      const t = queue[head++];
      for (let k = adj.start[t]; k < adj.start[t + 1]; k++) {
        const u = adj.list[k];
        if (out[u] !== NONE || (landOnly && !c.isLand(u))) continue;
        out[u] = out[t]; queue[tail++] = u;
      }
    }
  };

  for (let t = 0; t < n; t++) {
    const s = c.owner.length ? c.polityStyle.get(c.owner[t]) : undefined;
    if (s !== undefined && s < NONE) { out[t] = s; queue[tail++] = t; }
  }
  spread(true);

  // Landmasses no realm reached: one style each, drawn from the seed and the component's first tile.
  const stack: number[] = [];
  for (let t = 0; t < n; t++) {
    if (out[t] !== NONE || !c.isLand(t)) continue;
    const style = hash2(c.seed, t) % c.styleCount;
    out[t] = style; stack.push(t);
    while (stack.length) {
      const v = stack.pop()!;
      for (let k = adj.start[v]; k < adj.start[v + 1]; k++) {
        const u = adj.list[k];
        if (out[u] === NONE && c.isLand(u)) { out[u] = style; stack.push(u); }
      }
    }
  }

  // Water takes the nearest land's style.
  head = tail = 0;
  for (let t = 0; t < n; t++) if (out[t] !== NONE) queue[tail++] = t;
  spread(false);
  for (let t = 0; t < n; t++) if (out[t] === NONE) out[t] = 0;
  return out;
}

/** The style index of a polity's naming style among the chosen ones, or undefined when none matches. */
export function polityStyleIndex(styles: readonly NameStyle[], race: string, culture: string, folk: string, seed: number, polity: number): number | undefined {
  if (race) {
    const exact = styles.findIndex((s) => s.race === race && s.culture === culture);
    if (exact >= 0) return exact;
  }
  const of: number[] = [];
  styles.forEach((s, i) => { if (s.race === (race || folk)) of.push(i); });
  return of.length ? of[hash2(seed, 0x5017 + polity) % of.length] : undefined;
}
