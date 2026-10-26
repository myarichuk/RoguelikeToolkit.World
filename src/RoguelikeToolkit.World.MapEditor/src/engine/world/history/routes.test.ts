import { describe, expect, it } from 'vitest';
import { event, expansion } from './expansionFixture';
import { decodeRouteStates, havensAt, outlawHavens, raidsAt, routeKey, routeOfEvent, routeSafetyAt, safetyColor } from './routes';

describe('routes', () => {
  it('decodes route states with shares scaled back to 0..1', () => {
    const [r] = decodeRouteStates(expansion({ routeStates: Int32Array.of(10, 20, 620, 150, 2, 1, 4, 1, 7, 8) }));
    expect(r).toEqual({ a: 10, b: 20, safety: 0.62, traffic: 0.15, forts: 2, waystations: 1, outlawBand: 4, sponsor: 1, sabotagedTurn: 7, lastIncidentTurn: 8 });
  });

  it('reads each route at its latest sample up to the turn', () => {
    // turn, a, b, safety, traffic: by turn, then route.
    const series = Int32Array.of(1, 10, 20, 500, 0, 1, 30, 40, 400, 0, 2, 10, 20, 300, 100, 4, 10, 20, 900, 200, 4, 30, 40, 100, 50);
    expect(routeSafetyAt(series, 0).size).toBe(0);
    expect(routeSafetyAt(series, 1).get('10-20')).toEqual({ safety: 0.5, traffic: 0 });
    const at3 = routeSafetyAt(series, 3);
    expect(at3.get('10-20')!.safety).toBe(0.3);
    expect(at3.get('30-40')!.safety).toBe(0.4);
    expect(routeSafetyAt(series, 99).get('30-40')).toEqual({ safety: 0.1, traffic: 0.05 });
  });

  it('keys routes the way event tags write them, smaller tile first', () => {
    expect(routeKey(20, 10)).toBe('10-20');
    expect(routeOfEvent(event(1, 'CaravanRaided', 2, ['route:10-20']))).toBe('10-20');
    expect(routeOfEvent(event(1, 'CaravanRaided', 2, ['brigands']))).toBeUndefined();
  });

  it('places outlaw havens from OutlawRose and ends them with OutlawEnded', () => {
    const havens = outlawHavens([
      event(1, 'OutlawRose', 2, ['person:4', 'source:deserters', 'haven:77', 'route:10-20', 'from:1']),
      event(2, 'OutlawRose', 3, ['person:5', 'source:displaced', 'haven:88', 'route:30-40']),
      event(3, 'OutlawEnded', 6, ['person:4', 'end:hanged', 'route:10-20']),
    ]);
    expect(havens).toEqual([{ tile: 77, person: 4, route: '10-20', rose: 2, ended: 6 }, { tile: 88, person: 5, route: '30-40', rose: 3, ended: -1 }]);
    expect(havensAt(havens, 1)).toEqual([]);
    expect(havensAt(havens, 5).map((h) => h.person)).toEqual([4, 5]);
    expect(havensAt(havens, 6).map((h) => h.person)).toEqual([5]);
  });

  it('pins the caravan raids of one turn, a band\'s and nameless brigands\'', () => {
    const events = [
      event(1, 'CaravanRaided', 2, ['route:10-20', 'band:4', 'victim:1']),
      event(2, 'CaravanRaided', 2, ['route:30-40', 'brigands', 'victim:0']),
      event(3, 'CaravanRaided', 3, ['route:10-20', 'band:4', 'victim:1']),
    ];
    expect(raidsAt(events, 2)).toEqual([
      { tile: 1, route: '10-20', band: 4, victim: 1, event: 1 },
      { tile: 1, route: '30-40', band: -1, victim: 0, event: 2 },
    ]);
    expect(raidsAt(events, 4)).toEqual([]);
  });

  it('runs the safety heat from red to green', () => {
    expect(safetyColor(0)).toContain('hsla(0,');
    expect(safetyColor(1)).toContain('hsla(120,');
    expect(safetyColor(2)).toBe(safetyColor(1));
  });
});
