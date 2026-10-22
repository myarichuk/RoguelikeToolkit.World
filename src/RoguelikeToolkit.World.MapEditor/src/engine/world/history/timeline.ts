/**
 * What the map shows when the history scrubber is held back at a turn. A view turn of -1 means the end of history,
 * the map as generated: nothing is filtered, whatever the turns say.
 */

/** The turns a thing stood: from `from`, up to but not including `until` (-1 = still standing at the end). */
export interface Span {
  from: number;
  until: number;
}

/** A span that begins at `turn` and never ends. */
export const since = (turn: number): Span => ({ from: turn, until: -1 });

/** Whether something with this span exists at `turn`. Things with no span (rivers, mountains) always do. */
export const existsAt = (span: Span | undefined, turn: number): boolean =>
  turn < 0 || !span || (span.from <= turn && (span.until < 0 || turn < span.until));

export type RoadState = 'absent' | 'open' | 'cut';

/** A road before it was built is absent; after a war cut it, it is drawn cut. Roads with no build turn were always there. */
export function roadStateAt(road: { founded?: number; severed: number }, turn: number): RoadState {
  if (turn < 0) return road.severed >= 0 ? 'cut' : 'open';
  if ((road.founded ?? 0) > turn) return 'absent';
  return road.severed >= 0 && road.severed <= turn ? 'cut' : 'open';
}
