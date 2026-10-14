import { describe, expect, it } from 'vitest';
import type { Theme } from '../content/types';
import { MapDocument } from '../doc/MapDocument';
import type { FeatureEntity, LabelEntity } from '../doc/types';
import type { FontLibrary } from '../text/FontLibrary';
import { layoutLabel } from '../text/LabelLayout';
import { labelLandFraction, placeLabels } from './placeLabels';

// Glyphs are axis-aligned boxes, so laid-out bounds are predictable without a real font.
const boxGlyph = (adv: number) => ({
  index: 1,
  advanceWidth: adv,
  path: {},
  getPath: (_x: number, _y: number, fontSize: number) => {
    const s = fontSize / 1000;
    return {
      commands: [
        { type: 'M', x: 0, y: 0 },
        { type: 'L', x: adv * s, y: 0 },
        { type: 'L', x: adv * s, y: -700 * s },
        { type: 'L', x: 0, y: -700 * s },
        { type: 'Z' },
      ],
    };
  },
});

const stubFont = {
  unitsPerEm: 1000,
  tables: { os2: { sCapHeight: 700 } },
  charToGlyph: () => boxGlyph(600),
  getKerningValue: () => 0,
};
const stubFonts = { get: () => stubFont, version: 0 } as unknown as FontLibrary;

const theme = {
  labels: {
    sea: {
      font: 'stub', size: 15, spacing: 0.1, uppercase: true,
      fill: '#000', halo: '#fff', haloWidth: 0, minZoom: 0, sizeExp: 0,
    },
  },
} as unknown as Theme;

/** Open water with one land bar across it; the sea anchor sits mid-bar. */
function straitDoc(): MapDocument {
  const doc = MapDocument.empty();
  for (let y = 690; y < 710; y++) {
    for (let x = 0; x < doc.width; x++) doc.elevation[y * doc.width + x] = 0.3;
  }
  const sea: FeatureEntity = {
    id: 'f_sea:9', kind: 'sea', name: 'SEAWATER', x: 1000, y: 700,
    angle: 0, extent: 200, weight: 5, details: [], culture: '',
  };
  doc.features.add([sea]);
  return doc;
}

/** Share of the placed label's own glyph footprint reading as land. */
function placedLandFraction(doc: MapDocument, label: LabelEntity): number {
  const geo = layoutLabel(stubFont as never, {
    text: label.text, size: label.size, spacing: 0.1, uppercase: true,
    x: label.x, y: label.y, angle: label.angle, curve: label.curve, path: label.path,
  });
  return labelLandFraction((x, y) => doc.elevationAt(x, y), geo.bounds, 4);
}

describe('labelLandFraction', () => {
  it('measures the share of land over a rect', () => {
    const doc = MapDocument.empty(); // all water
    expect(labelLandFraction((x, y) => doc.elevationAt(x, y), { x0: 0, y0: 0, x1: 10, y1: 10 }, 5)).toBe(0);
    doc.elevation.fill(0.3); // all land
    expect(labelLandFraction((x, y) => doc.elevationAt(x, y), { x0: 0, y0: 0, x1: 10, y1: 10 }, 5)).toBe(1);
  });
});

describe('sea labels avoid land', () => {
  it('refuses the centered candidate on the land bar and places a water fallback', () => {
    const doc = straitDoc();
    const labels = placeLabels(doc, theme, stubFonts, 11);
    expect(labels).toHaveLength(1);
    // The centered arched candidate sits on the bar; a shifted fallback over water must win.
    expect(Math.abs(labels[0].y - 700)).toBeGreaterThan(5);
    expect(placedLandFraction(doc, labels[0])).toBeLessThanOrEqual(0.5);
  });
});

describe('river labels on short reaches', () => {
  it('extends an overlong name past the segment instead of piling glyphs at its ends', () => {
    const geo = layoutLabel(stubFont as never, {
      text: 'AVERYLONG RIVERNAME', size: 5, spacing: 0.12, uppercase: false,
      x: 5, y: 0, angle: 0, curve: 0, path: [0, 0, 10, 0],
    });
    // The 10-unit segment carries ~97 units of text: piled glyphs would span ~14 units.
    expect(geo.bounds.x1 - geo.bounds.x0).toBeGreaterThan(30);
    expect(geo.bounds.x0).toBeLessThan(0);
    expect(geo.bounds.x1).toBeGreaterThan(10);
  });
});
