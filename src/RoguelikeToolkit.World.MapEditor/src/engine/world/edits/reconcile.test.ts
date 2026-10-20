import { describe, expect, it } from 'vitest';
import type { FeatureEntity, RiverEntity } from '../../doc/types';
import { reconcileFeatures } from './reconcile';

const area = (id: string, kind: FeatureEntity['kind'], tiles: number[], name = '', request = true): FeatureEntity => ({
  id, kind, name, x: 0, y: 0, angle: 0, extent: 0, weight: tiles.length, tiles, details: [], culture: '',
  request: request ? { kind, seed: 1, race: 'human', culture: 'c', context: 0 } : undefined,
});
const range = (a: number, b: number) => Array.from({ length: b - a }, (_, i) => a + i);
const river = (id: string, name: string, x0: number, n: number): RiverEntity => ({ id, name, points: Array.from({ length: n * 3 }, (_, i) => (i % 3 === 0 ? x0 + i / 3 * 2 : i % 3 === 1 ? 50 : 1)) });
const riverFeature = (id: string, riverId: string, name = ''): FeatureEntity => ({ ...area(id, 'river', [], name), tiles: undefined, riverId });
const none = { live: [] as FeatureEntity[], rivers: new Map<string, RiverEntity>(), tombstones: [] as FeatureEntity[], next: { features: [] as FeatureEntity[], rivers: [] as RiverEntity[] } };

describe('reconcileFeatures', () => {
  it('keeps the identity and the name of a lake whose id shifted', () => {
    const old = area('f_lake:2', 'lake', range(0, 10), 'Silverfen');
    const r = reconcileFeatures({ ...none, live: [old], next: { features: [area('f_lake:5', 'lake', range(2, 12))], rivers: [] } });
    expect(r.features).toHaveLength(1);
    expect(r.features[0].id).toBe('f_lake:2');
    expect(r.features[0].name).toBe('Silverfen');
    expect(r.pending).toEqual([]);
    expect(r.kept).toBe(1);
  });

  it('treats an unrelated body as new and asks for its name', () => {
    const r = reconcileFeatures({ ...none, live: [area('f_lake:1', 'lake', range(0, 5), 'Old')], next: { features: [area('f_lake:1', 'lake', range(100, 105))], rivers: [] } });
    expect(r.features).toHaveLength(1);
    expect(r.features[0].name).toBe('');
    expect(r.pending).toEqual([r.features[0].id]);
    expect(r.removed).toBe(1);
    expect(r.added).toBe(1);
  });

  it('never reuses an id that a matched feature holds', () => {
    const live = [area('f_lake:1', 'lake', range(0, 5), 'Kept')];
    const next = [area('f_lake:2', 'lake', range(0, 5)), area('f_lake:1', 'lake', range(100, 105))];
    const r = reconcileFeatures({ ...none, live, next: { features: next, rivers: [] } });
    const ids = r.features.map((f) => f.id);
    expect(new Set(ids).size).toBe(2);
    expect(r.features.find((f) => f.name === 'Kept')!.id).toBe('f_lake:1');
  });

  it('keeps a deleted feature deleted when it reappears, and tracks its new shape', () => {
    const tomb = area('f_forest:9', 'forest', range(0, 20), 'Gone');
    const r = reconcileFeatures({ ...none, tombstones: [tomb], next: { features: [area('f_forest:4', 'forest', range(5, 25))], rivers: [] } });
    expect(r.features).toEqual([]);
    expect(r.tombstones).toHaveLength(1);
    expect(r.tombstones[0].id).toBe('f_forest:9');
    expect(r.tombstones[0].tiles).toEqual(range(5, 25));
  });

  it('forgets a tombstone whose feature no longer exists', () => {
    const r = reconcileFeatures({ ...none, tombstones: [area('f_forest:9', 'forest', range(0, 5))], next: { features: [], rivers: [] } });
    expect(r.tombstones).toEqual([]);
  });

  it('a sea may become an ocean and still be itself, but a lake never becomes a mountain', () => {
    const live = [area('f_sea:1', 'sea', range(0, 30), 'Mare'), area('f_lake:1', 'lake', range(50, 60), 'Mere')];
    const next = [area('f_sea:7', 'ocean', range(0, 30)), area('f_range:1', 'mountain', range(50, 60))];
    const r = reconcileFeatures({ ...none, live, next: { features: next, rivers: [] } });
    expect(r.features.find((f) => f.kind === 'ocean')!.name).toBe('Mare');
    expect(r.features.find((f) => f.kind === 'mountain')!.name).toBe('');
  });

  it('matches a re-routed river by the bed it still follows and carries its name onto the new ribbon', () => {
    const oldR = river('river_a', 'Anduin', 0, 30);
    const live = [riverFeature('f_river:0', 'river_a', 'Anduin')];
    const newR = river('river_new', '', 4, 30);          // same line, shifted upstream end
    const r = reconcileFeatures({ ...none, live, rivers: new Map([[oldR.id, oldR]]), next: { features: [riverFeature('f_river:3', 'river_new')], rivers: [newR] } });
    expect(r.features[0].id).toBe('f_river:0');
    expect(r.features[0].name).toBe('Anduin');
    expect(r.rivers).toHaveLength(1);
    expect(r.rivers[0].id).toBe('river_new');
    expect(r.rivers[0].name).toBe('Anduin');
    expect(r.features[0].riverId).toBe('river_new');
  });

  it('does not confuse two distant rivers', () => {
    const oldR = river('river_a', 'Anduin', 0, 30);
    const farR = river('river_far', '', 900, 30);
    const r = reconcileFeatures({ ...none, live: [riverFeature('f_river:0', 'river_a', 'Anduin')], rivers: new Map([[oldR.id, oldR]]), next: { features: [riverFeature('f_river:1', 'river_far')], rivers: [farR] } });
    expect(r.features[0].name).toBe('');
    expect(r.removed).toBe(1);
  });

  it('settlements keep their identity by site even when their rank changes', () => {
    const old: FeatureEntity = { ...area('f_settlement:77', 'village', [], 'Hobbiton'), tiles: undefined };
    const nw: FeatureEntity = { ...area('f_settlement:77', 'city', [], ''), tiles: undefined };
    const r = reconcileFeatures({ ...none, live: [old], next: { features: [nw], rivers: [] } });
    expect(r.features[0].kind).toBe('city');
    expect(r.features[0].name).toBe('Hobbiton');
  });

  it('matches deposits of the same type that sit close together', () => {
    const dep = (id: string, x: number, type: 'iron' | 'gold', name = ''): FeatureEntity => ({ ...area(id, 'deposit', [], name, false), tiles: undefined, x, y: 10, deposit: { type, richness: 1 } });
    const live = [dep('f_deposit:1', 100, 'iron', 'Ironrise'), dep('f_deposit:2', 300, 'gold', 'Goldvein')];
    const next = [dep('f_deposit:8', 103, 'iron'), dep('f_deposit:9', 300, 'iron')];
    const r = reconcileFeatures({ ...none, live, next: { features: next, rivers: [] } });
    expect(r.features.find((f) => f.x === 103)!.name).toBe('Ironrise');
    expect(r.features.find((f) => f.x === 300)!.name).toBe('');
  });
});
