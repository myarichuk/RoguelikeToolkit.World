import { describe, expect, it } from 'vitest';
import { MAP_H, MAP_W } from '../../core/geo';
import { MapDocument } from '../../doc/MapDocument';
import { decodeDocument, encodeDocument } from '../../doc/serialize';
import { DEFAULT_SETTINGS } from '../../doc/types';
import { event, expansion } from './expansionFixture';
import { EXPANSION_ARRAYS, eventLine, ownersAt, standsAt, type EventRecord, type HistoryRecord, type PolityRecord } from './record';

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

  const docWith = (h: HistoryRecord) => {
    const doc = new MapDocument({ ...DEFAULT_SETTINGS }, new Float32Array(MAP_W * MAP_H), new Uint8Array(MAP_W * MAP_H),
      { tile: new Int32Array(MAP_W * MAP_H).fill(-1), tilePlate: new Uint32Array(3), plateDrift: new Float64Array(0), tileXY: new Float32Array(6), styles: [] });
    doc.history = h;
    return doc;
  };
  const headerOf = (bytes: Uint8Array) => JSON.parse(new TextDecoder().decode(bytes.subarray(12, 12 + new DataView(bytes.buffer).getUint32(8, true))));

  it('writes a history without the expansion exactly as before: no expansion key, no expansion blobs', () => {
    const header = headerOf(encodeDocument(docWith(record())));
    expect(Object.keys(header.history)).toEqual(['turns', 'turnYears', 'polities', 'events']);
    expect(header.blobs.some((b: { name: string }) => b.name.startsWith('history.'))).toBe(false);
  });

  it('round-trips the expansion and event participants through a .wmap', () => {
    const x = expansion({ battles: Int32Array.of(...new Array(24).fill(7)), battleText: ['open\t\t\t\t\t'], chronicle: ['In the winter of year 23 the hosts met.'] });
    const e = event(1, 'War', 2, ['war', 'battle:7'], [{ entity: 1, role: 'person:attacker_general' }]);
    const back = decodeDocument(encodeDocument(docWith({ ...record(), events: [e], expansion: x }))).history!;
    for (const k of EXPANSION_ARRAYS) expect([...back.expansion![k]]).toEqual([...x[k]]);
    expect(back.expansion!.personNames).toEqual(x.personNames);
    expect(back.expansion!.chronicle).toEqual(x.chronicle);
    expect(back.events[0].parts).toEqual([{ entity: 1, role: 'person:attacker_general' }]);
  });

  it('words the expansion kinds, naming persons through the resolver', () => {
    const realm = (id: number) => `Realm ${id}`, person = (id: number) => `Aldric ${id}`;
    expect(eventLine(event(1, 'War', 2, ['war', 'attacker:0', 'defender:1', 'outcome:defender_decisive']), realm)).toBe('Realm 0 attacked Realm 1, ending in a rout of the attackers');
    expect(eventLine(event(2, 'PersonRose', 2, ['person:4', 'office:marshal']), realm, person)).toBe('Aldric 4 became marshal of Realm 0');
    expect(eventLine(event(3, 'PersonDied', 2, ['person:4', 'battle', 'office:ruler']), realm, person)).toBe('Aldric 4 of Realm 0 died (battle)');
    expect(eventLine(event(4, 'CaravanRaided', 2, ['route:1-2', 'band:5', 'victim:1']), realm, person)).toBe('Aldric 5\'s band raided a caravan of Realm 1');
    expect(eventLine(event(5, 'CaravanRaided', 2, ['route:1-2', 'brigands', 'victim:1']), realm)).toBe('Brigands raided a caravan of Realm 1');
    expect(eventLine(event(6, 'Intrigue', 2, ['plot:sabotage', 'actor:1', 'target:0', 'exposed']), realm)).toBe('Realm 1 was caught plotting (sabotage) against Realm 0');
  });
});
