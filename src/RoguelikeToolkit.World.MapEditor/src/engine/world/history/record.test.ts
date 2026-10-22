import { describe, expect, it } from 'vitest';
import { MAP_H, MAP_W } from '../../core/geo';
import { MapDocument } from '../../doc/MapDocument';
import { decodeDocument, encodeDocument } from '../../doc/serialize';
import { DEFAULT_SETTINGS } from '../../doc/types';
import { eventLine, ownersAt, standsAt, type EventRecord, type HistoryRecord, type PolityRecord } from './record';

const polity = (id: number, founded: number, ended: number): PolityRecord => ({ id, form: 'Kingdom', capital: 0, founded, ended, parent: -1, endCause: '', folk: 'human', culture: 'human_common', overlay: '', name: `Realm ${id}`, style: 0 });
const record = (): HistoryRecord => ({
  turns: 5, turnYears: 10, polities: [polity(0, 0, -1), polity(1, 2, 4)], events: [],
  ownerBase: Int32Array.of(0, 0, -1), changes: Int32Array.of(2, 2, 1, 4, 1, 0),
});

describe('history record', () => {
  it('replays territory changes up to a turn', () => {
    expect([...ownersAt(record(), 0)]).toEqual([0, 0, -1]);
    expect([...ownersAt(record(), 2)]).toEqual([0, 0, 1]);
    expect([...ownersAt(record(), 5)]).toEqual([0, 0, 1]);
  });
  it('says when a realm stands', () => {
    const p = polity(1, 2, 4);
    expect([1, 2, 3, 4].map((t) => standsAt(p, t))).toEqual([false, true, true, false]);
  });
  it('writes events in plain words, naming both sides of a war', () => {
    const e: EventRecord = { id: 1, kind: 'War', turn: 3, polity: 0, tile: 5, cause: -1, importance: 1, plugin: '', tags: ['attacker:0', 'defender:1'] };
    expect(eventLine(e, (id) => `Realm ${id}`)).toBe('Realm 0 attacked Realm 1');
  });
  it('survives a .wmap round trip with its territory', () => {
    const doc = new MapDocument({ ...DEFAULT_SETTINGS }, new Float32Array(MAP_W * MAP_H), new Uint8Array(MAP_W * MAP_H),
      { tile: new Int32Array(MAP_W * MAP_H).fill(-1), tilePlate: new Uint32Array(3), tileStyle: Uint8Array.of(0, 1, 0), plateDrift: new Float64Array(0), tileXY: new Float32Array(6), styles: [] });
    doc.history = record();
    const back = decodeDocument(encodeDocument(doc));
    expect(back.history!.polities.map((p) => p.name)).toEqual(['Realm 0', 'Realm 1']);
    expect([...back.history!.changes]).toEqual([2, 2, 1, 4, 1, 0]);
    expect([...back.world.tileStyle!]).toEqual([0, 1, 0]);
    expect(decodeDocument(encodeDocument(MapDocument.empty())).history).toBeNull();
  });
});
