// After a recompute the engine hands back a whole new feature set whose ids are ordinal ("lake:3",
// "range:11") and shift whenever the world does. The document, though, holds identities: names the
// author chose, labels attached to a feature, features the author deleted. This module carries those
// across by *matching* new features to old ones on what they are (shared tiles, shared riverbed,
// same place) rather than on their ids, so "the Silverfen lake" is still the Silverfen after a
// ridge is raised beside it, and a deleted feature stays deleted.
import type { FeatureEntity, RiverEntity } from '../doc/types';
import type { FeatureKind } from './protocol';

const AREA_FAMILY: Partial<Record<FeatureKind, string>> = {
  lake: 'lake', sea: 'sea', ocean: 'sea', mountain: 'mountain', valley: 'valley', forest: 'forest',
};
const SETTLEMENTS: ReadonlySet<FeatureKind> = new Set(['capital', 'city', 'town', 'village', 'ruin', 'shrine', 'battlefield']);

/** Shared-tile share needed for two area features to be the same thing. */
export const AREA_MATCH = 0.25;
/** Share of one river's vertices lying on the other (both ways, the lesser counts). */
export const RIVER_MATCH = 0.4;
/** A river vertex is "on" the other river within this many map units. */
export const RIVER_NEAR = 4;
/** Deposits of one type this close are the same deposit (placement jitters by up to ~1.5 mu around the tile). */
export const DEPOSIT_NEAR = 8;

export interface ReconcileInput {
  /** Generated-owned features the document currently has. */
  live: readonly FeatureEntity[];
  /** The document's river entities (looked up by `riverId`). */
  rivers: ReadonlyMap<string, RiverEntity>;
  /** Generated features the author deleted (geometry kept so they can be recognised again). */
  tombstones: readonly FeatureEntity[];
  /** The fresh extraction, with its own river entities. */
  next: { features: readonly FeatureEntity[]; rivers: readonly RiverEntity[] };
}

export interface ReconcileResult {
  features: FeatureEntity[];
  rivers: RiverEntity[];
  tombstones: FeatureEntity[];
  /** New features that still need a name (they carry a name request). */
  pending: string[];
  kept: number;
  added: number;
  removed: number;
}

interface Candidate { score: number; a: number; b: number; }

function jaccard(a: readonly number[], b: readonly number[]): number {
  if (!a.length || !b.length) return 0;
  const small = a.length <= b.length ? a : b, big = small === a ? b : a;
  const set = new Set(big);
  let inter = 0;
  for (const t of small) if (set.has(t)) inter++;
  return inter / (a.length + b.length - inter);
}

/** Share of `a`'s vertices lying within RIVER_NEAR of any vertex of `b` (grid-hashed). */
function coverage(a: readonly number[], b: readonly number[]): number {
  const cell = RIVER_NEAR;
  const grid = new Map<string, true>();
  for (let i = 0; i < b.length; i += 3) grid.set(`${Math.floor(b[i] / cell)},${Math.floor(b[i + 1] / cell)}`, true);
  let hit = 0, n = 0;
  for (let i = 0; i < a.length; i += 3, n++) {
    const cx = Math.floor(a[i] / cell), cy = Math.floor(a[i + 1] / cell);
    let found = false;
    for (let dx = -1; dx <= 1 && !found; dx++) for (let dy = -1; dy <= 1 && !found; dy++) if (grid.has(`${cx + dx},${cy + dy}`)) found = true;
    if (found) hit++;
  }
  return n ? hit / n : 0;
}

/** Wrap-aware x distance on the 360° map is ignored: river points are unwrapped per river, and a seam crossing only costs a missed match. */
export function reconcileFeatures(input: ReconcileInput): ReconcileResult {
  const pool = [...input.live.map((f) => ({ f, dead: false })), ...input.tombstones.map((f) => ({ f, dead: true }))];
  const next = input.next.features;
  const nextRivers = new Map(input.next.rivers.map((r) => [r.id, r]));
  const used = new Set<number>(), taken = new Set<number>();
  const match = new Map<number, number>(); // next index -> pool index

  const claim = (b: number, a: number) => { match.set(b, a); used.add(a); taken.add(b); };

  // 1) Settlements keep their identity by tile: the id is the site, whatever rank it now has.
  const poolById = new Map<string, number>();
  pool.forEach((p, i) => poolById.set(p.f.id, i));
  next.forEach((n, b) => {
    if (!SETTLEMENTS.has(n.kind)) return;
    const a = poolById.get(n.id);
    if (a !== undefined && SETTLEMENTS.has(pool[a].f.kind) && !used.has(a)) claim(b, a);
  });

  // 2) Everything else: best overlap first, one-to-one.
  const cands: Candidate[] = [];
  pool.forEach((p, a) => {
    if (used.has(a)) return;
    next.forEach((n, b) => {
      if (taken.has(b)) return;
      const score = similarity(p.f, n, input.rivers, nextRivers);
      if (score > 0) cands.push({ score, a, b });
    });
  });
  cands.sort((x, y) => y.score - x.score || x.a - y.a || x.b - y.b);
  for (const c of cands) if (!used.has(c.a) && !taken.has(c.b)) claim(c.b, c.a);

  const features: FeatureEntity[] = [], rivers: RiverEntity[] = [], tombstones: FeatureEntity[] = [], pending: string[] = [];
  const ids = new Set<string>();
  const unique = (id: string) => { let u = id, k = 2; while (ids.has(u)) u = `${id}~${k++}`; ids.add(u); return u; };
  // Matched features keep the old id; reserve those first so a new feature can never steal one.
  for (const [, a] of match) if (!pool[a].dead) ids.add(pool[a].f.id);
  let kept = 0, added = 0;

  next.forEach((n, b) => {
    const a = match.get(b);
    if (a !== undefined && pool[a].dead) { tombstones.push({ ...n, id: pool[a].f.id }); return; }
    const out: FeatureEntity = a !== undefined
      ? { ...n, id: pool[a].f.id, name: pool[a].f.name, request: pool[a].f.request ?? n.request }
      : { ...n, id: unique(n.id) };
    if (n.riverId) {
      const r = nextRivers.get(n.riverId);
      if (r) { rivers.push({ ...r, name: out.name }); out.riverId = r.id; }
    }
    features.push(out);
    if (a !== undefined) kept++; else { added++; if (out.request) pending.push(out.id); }
  });

  const removed = pool.filter((p, a) => !p.dead && !used.has(a)).length;
  return { features, rivers, tombstones, pending, kept, added, removed };
}

function similarity(old: FeatureEntity, nw: FeatureEntity, oldRivers: ReadonlyMap<string, RiverEntity>, newRivers: ReadonlyMap<string, RiverEntity>): number {
  if (old.kind === 'river' && nw.kind === 'river') {
    const a = old.riverId && oldRivers.get(old.riverId), b = nw.riverId && newRivers.get(nw.riverId);
    if (!a || !b) return 0;
    const s = Math.min(coverage(b.points, a.points), coverage(a.points, b.points));
    return s >= RIVER_MATCH ? s : 0;
  }
  if (old.kind === 'deposit' && nw.kind === 'deposit') {
    if (old.deposit?.type !== nw.deposit?.type) return 0;
    const d = Math.hypot(old.x - nw.x, old.y - nw.y);
    return d <= DEPOSIT_NEAR ? 1 - d / (DEPOSIT_NEAR * 2) : 0;
  }
  const fa = AREA_FAMILY[old.kind], fb = AREA_FAMILY[nw.kind];
  if (fa && fa === fb && old.tiles && nw.tiles) {
    const s = jaccard(old.tiles, nw.tiles);
    return s >= AREA_MATCH ? s : 0;
  }
  return 0;
}
