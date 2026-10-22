import type { Actor, ActorStats } from './actors';

/** A title an actor earned, with the line that says why. */
export interface Achievement {
  rule: string;
  title: string;
  text: string;
  actor: string;
  /** The turn it can be pinned to on the timeline, when it has one. */
  turn?: number;
}

/**
 * One way to earn a title: the best actor by a figure takes it, and actors tied for the best share it.
 * A rule is plain data: it names the figure and how to read it, so adding a rule is adding a row.
 *
 * TODO: make it scriptable. This fixed set of rules should become data or scripts the world's author can add to (the same
 * way history plugins hook the simulation), so a campaign can award its own titles, "Sacker of Ashgard" or "Last of the
 * Old Kings", without a code change here.
 */
export interface AchievementRule {
  id: string;
  title: string;
  /** The figure to rank on; null means the actor is not in the running. */
  value(stats: ActorStats): number | null;
  /** Whether the highest or the lowest value wins. */
  best: 'max' | 'min';
  /** The line under the title. */
  text(actor: Actor, stats: ActorStats, value: number): string;
  /** The turn to pin the title to on the timeline. */
  turn?(stats: ActorStats, value: number): number | undefined;
}

const atLeast = (n: number, v: number): number | null => (v >= n ? v : null);
const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

export const ACHIEVEMENT_RULES: readonly AchievementRule[] = [
  {
    id: 'largest', title: 'Greatest extent', best: 'max',
    value: (s) => atLeast(1, s.peakTiles),
    text: (_, s, v) => `Held ${plural(v, 'tile')} at its height, in turn ${s.peakTurn}.`,
    turn: (s) => s.peakTurn,
  },
  {
    id: 'enduring', title: 'Most enduring', best: 'max',
    value: (s) => atLeast(1, s.turnsStanding),
    text: (_, __, v) => `Stood for ${plural(v, 'turn')}.`,
  },
  {
    id: 'conqueror', title: 'Conqueror', best: 'max',
    value: (s) => atLeast(1, s.conquests),
    text: (_, __, v) => `Won ${plural(v, 'conquest')}, taking rivals under its culture.`,
  },
  {
    id: 'warlike', title: 'Warmonger', best: 'max',
    value: (s) => atLeast(2, s.wars),
    text: (_, __, v) => `Started ${plural(v, 'war')}.`,
  },
  {
    id: 'colonist', title: 'Great colonist', best: 'max',
    value: (s) => atLeast(2, s.settlementsFounded),
    text: (_, __, v) => `Founded ${plural(v, 'settlement')}.`,
  },
  {
    id: 'spoils', title: 'Spoils of war', best: 'max',
    value: (s) => atLeast(1, s.settlementsTaken),
    text: (_, __, v) => `Took ${plural(v, 'settlement')} from rivals.`,
  },
  {
    id: 'devout', title: 'Most devout', best: 'max',
    value: (s) => atLeast(1, s.holySites),
    text: (_, __, v) => `Raised ${plural(v, 'holy site')}.`,
  },
  {
    id: 'brood', title: 'Mother of realms', best: 'max',
    value: (s) => atLeast(1, s.breakaways),
    text: (_, __, v) => `${plural(v, 'realm')} broke away from it.`,
  },
  {
    id: 'first-fall', title: 'First to fall', best: 'min',
    value: (s) => s.fell,
    text: (_, __, v) => `Was gone by turn ${v}.`,
    turn: (_, v) => v,
  },
];

/**
 * Who earned what. An actor earns every title it leads, ties included. A title is a distinction, so with a single actor
 * there is none to give, and one every actor shares (everyone stood the whole run) is left unawarded.
 */
export function awardAchievements(actors: readonly Actor[], stats: ReadonlyMap<string, ActorStats>, rules: readonly AchievementRule[] = ACHIEVEMENT_RULES): Achievement[] {
  const out: Achievement[] = [];
  if (actors.length < 2) return out;
  for (const rule of rules) {
    const ranked = actors.flatMap((a) => {
      const s = stats.get(a.id), v = s ? rule.value(s) : null;
      return s && v !== null ? [{ a, s, v }] : [];
    });
    if (!ranked.length) continue;
    const best = ranked.reduce((m, r) => (rule.best === 'max' ? Math.max(m, r.v) : Math.min(m, r.v)), rule.best === 'max' ? -Infinity : Infinity);
    const leaders = ranked.filter((r) => r.v === best);
    if (leaders.length === actors.length) continue;
    for (const r of leaders) {
      out.push({ rule: rule.id, title: rule.title, text: rule.text(r.a, r.s, r.v), actor: r.a.id, turn: rule.turn?.(r.s, r.v) });
    }
  }
  return out;
}
