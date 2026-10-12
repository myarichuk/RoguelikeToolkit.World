'use strict';
// Generate -> determinism -> .wmap save/open round trip.
const { run } = require('./harness.cjs');

run('generate, determinism, save/open', async (t) => {
  const hash = () => t.evaluate(() => {
    const d = window.editor.doc;
    let h = 0x811c9dc5;
    for (const a of [d.biome, d.landuse]) for (let i = 0; i < a.length; i++) h = Math.imul(h ^ a[i], 16777619);
    for (let i = 0; i < d.width * d.height; i += 7) h = Math.imul(h ^ Math.round(d.elevationAt(i % d.width, (i / d.width) | 0) * 1000), 16777619);
    return h >>> 0;
  });

  await t.generate({ seed: 42, size: 3 });
  const dims = await t.evaluate(() => ({ w: window.editor.doc.width, h: window.editor.doc.height, features: window.editor.doc.features.size, tiles: window.editor.doc.world.tileXY.length / 2 }));
  t.check('a generated world has raster dimensions, tiles and features', dims.w > 0 && dims.h > 0 && dims.tiles > 0 && dims.features > 0, dims);

  const a = await hash();
  await t.generate({ seed: 42, size: 3 });
  t.check('the same seed generates the same world', (await hash()) === a);
  await t.generate({ seed: 43, size: 3 });
  t.check('a different seed generates a different world', (await hash()) !== a);

  await t.generate({ seed: 42, size: 3 });
  const before = await hash();
  const bytes = await t.evaluate(async () => { const b = await window.editor.save(); await window.editor.open(b); return b.size; });
  t.check('save produces a non-empty file', bytes > 0, bytes);
  t.check('open restores exactly what save wrote', (await hash()) === before);
});
