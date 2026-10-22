import { describe, expect, it } from 'vitest';
import { labelExistsAt, stampExistsAt } from './existsAt';
import { MapDocument } from './MapDocument';
import { labelIdOf, stampIdOf, type FeatureEntity } from './types';

const town = (id: string, from: number): FeatureEntity => ({ id, kind: 'town', name: 'T', x: 1, y: 1, angle: 0, extent: 0, weight: 1, details: [], culture: '', span: { from, until: -1 } });

describe('labels and stamps on a history timeline', () => {
  const doc = MapDocument.empty();
  doc.features.reset([town('f_settlement:5', 20)]);

  it('hides a place\'s label and stamp until the turn it was founded', () => {
    const label = labelIdOf('f_settlement:5'), stamp = stampIdOf('f_settlement:5');
    expect([10, 20].map((t) => labelExistsAt(doc, label, t))).toEqual([false, true]);
    expect([10, 20].map((t) => stampExistsAt(doc, stamp, t))).toEqual([false, true]);
    expect(labelExistsAt(doc, label, -1)).toBe(true);
  });

  it('never hides what is not a place: hand-placed labels and decoration stamps', () => {
    expect(labelExistsAt(doc, 'lb_hand_1', 0)).toBe(true);
    expect(stampExistsAt(doc, 'st_trees_3_4', 0)).toBe(true);
  });
});
