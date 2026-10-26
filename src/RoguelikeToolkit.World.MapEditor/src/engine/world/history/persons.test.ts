import { describe, expect, it } from 'vitest';
import { event, history } from './expansionFixture';
import { involvedPersons, livingAt, personIndex, personNameRequest, personsOfEvent, PERSON_STRIDE, RELATION_STRIDE, TENURE_STRIDE, type Person } from './persons';

describe('persons', () => {
  it('decodes the engine stride with its string tables', () => {
    const ix = personIndex(history())!;
    expect(ix.persons).toHaveLength(6);
    const ruler = ix.get(0)!, general = ix.get(1)!;
    expect(ruler).toMatchObject<Partial<Person>>({ kind: 'Ruler', polity: 0, diedTurn: 6, deathCause: 'age', epithets: ['cruel', 'cunning'], folk: 'human', culture: 'human_common', name: 'Name 0' });
    expect(general).toMatchObject<Partial<Person>>({ kind: 'General', feminine: true, wealth: 123.45, deathCause: '', epithets: [] });
    expect(ix.get(3)!.name).toBe('Person 3');
    expect([PERSON_STRIDE, RELATION_STRIDE, TENURE_STRIDE]).toEqual([25, 6, 5]);
  });

  it('says who lives at a turn, by the rule realms stand by', () => {
    const ruler = personIndex(history())!.get(0)!;
    expect([0, 5, 6, 7].map((t) => livingAt(ruler, t))).toEqual([true, true, false, false]);
    const ix = personIndex(history())!;
    expect(ix.personsOf(0).map((p) => p.id)).toEqual([0, 1]);
    expect(ix.personsOf(0, 1).map((p) => p.id)).toEqual([0]);
    expect(ix.living(4).map((p) => p.id)).toEqual([0, 1, 2, 3, 4, 5]);
  });

  it('indexes bonds and offices per person, and finds an office holder at a turn', () => {
    const ix = personIndex(history())!;
    expect(ix.relationsOf(1).map((r) => r.kind)).toEqual(['patron']);
    expect(ix.relationsOf(3).map((r) => r.kind).sort()).toEqual(['agent_of', 'sponsor']);
    expect(ix.tenuresOf(1).map((t) => t.office)).toEqual(['marshal', 'ruler']);
    expect(ix.holder(0, 'ruler', 3)?.id).toBe(0);
    expect(ix.holder(0, 'ruler', 7)?.id).toBe(1);
    expect(ix.holder(0, 'chancellor', 3)).toBeUndefined();
  });

  it('names the sponsor of an agent, a privateer and a sponsored band', () => {
    const plot = event(9, 'Intrigue', 5, ['plot:sponsorship', 'actor:1', 'target:0', 'person:4', 'secret']);
    const bribe = event(10, 'Intrigue', 6, ['plot:bribe', 'actor:2', 'target:0', 'person:1']);
    const ix = personIndex(history([bribe, plot]))!;
    expect(ix.sponsorOf(2)).toEqual({ polity: 1, person: 3 });
    expect(ix.sponsorOf(5)).toEqual({ polity: 1, person: 3 });
    expect(ix.sponsorOf(4)).toEqual({ polity: 1, person: -1 });
    expect(ix.sponsorOf(1)).toBeNull();
  });

  it('reads participants by the person: role prefix', () => {
    const e = event(1, 'War', 3, ['attacker:0', 'defender:1'], [{ entity: 1, role: 'person:attacker_general' }, { entity: 7, role: 'settlement:besieged' }, { entity: 3, role: 'person:defender_general' }]);
    expect(involvedPersons(e)).toEqual([1, 3]);
    expect(involvedPersons(e, 'defender_general')).toEqual([3]);
    expect(involvedPersons(event(2, 'War', 3, []))).toEqual([]);
    expect(personIndex(history([e]))!.eventsOf(3).map((x) => x.id)).toEqual([1]);
    const raid = event(3, 'CaravanRaided', 4, ['route:1-2', 'band:4', 'victim:1', 'merchant:5'], [{ entity: 4, role: 'person:outlaw' }]);
    expect(personsOfEvent(raid)).toEqual([4, 5]);
    expect(personsOfEvent(event(4, 'Intrigue', 4, ['plot:bribe', 'actor:1', 'target:0', 'person:1']))).toEqual([1]);
  });

  it('keys a name on the world seed and the person, deterministically', () => {
    const a = personNameRequest(42, 7, 'human', 'human_common', false);
    expect(personNameRequest(42, 7, 'human', 'human_common', false)).toEqual(a);
    expect(personNameRequest(42, 8, 'human', 'human_common', false).seed).not.toBe(a.seed);
    expect(personNameRequest(43, 7, 'human', 'human_common', false).seed).not.toBe(a.seed);
    expect(a.seed).toBeGreaterThanOrEqual(0);
  });

  it('has no index without an expansion, and caches it per record', () => {
    expect(personIndex(null)).toBeNull();
    expect(personIndex({ ...history(), expansion: undefined })).toBeNull();
    const h = history();
    expect(personIndex(h)).toBe(personIndex(h));
  });
});
