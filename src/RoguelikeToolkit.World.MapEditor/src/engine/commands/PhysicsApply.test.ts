import { describe, expect, it } from 'vitest';
import { MapDocument } from '../doc/MapDocument';
import { applyFrame, captureFrame } from './PhysicsApply';

describe('physics frames', () => {
  it('carry the land-capacity cache, so undoing an apply does not lose the overlay', () => {
    const doc = MapDocument.empty();
    const capacity = Uint8Array.from([1, 2, 3, 4, 5]);
    doc.physics = { edits: new Map(), baseline: null, generated: new Set(), tombstones: new Map(), capacity };
    const frame = captureFrame(doc);
    expect(frame.physics!.capacity).toBe(capacity);
    doc.physics = { edits: new Map(), baseline: null, generated: new Set(), tombstones: new Map(), capacity: Uint8Array.of(9) };
    applyFrame(doc, frame);
    expect(doc.physics!.capacity).toBe(capacity);
  });
});
