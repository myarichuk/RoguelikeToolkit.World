'use strict';
// Land use is drawn over the biome and never changes it, never moves physics, and round-trips.
const { run } = require('./harness.cjs');

// Land-use ids as the editor stores them: 1 farmland, 2 ashlands, 3 pasture (0 clears).

run('land-use brush and capacity', async (t) => {
  await t.generate({ seed: 42, size: 3 });
  const box = await t.canvasBox();
  const land = await t.landOnScreen(box);
  t.check('the view shows land to paint on', !!land);
  if (!land) return;

  const biomeHash = () => t.evaluate(() => { const b = window.editor.doc.biome; let h = 0; for (let i = 0; i < b.length; i++) h = (h * 31 + b[i]) | 0; return h; });
  const counts = () => t.evaluate(() => { const l = window.editor.doc.landuse; let f = 0, a = 0, p = 0; for (let i = 0; i < l.length; i++) { if (l[i] === 1) f++; else if (l[i] === 2) a++; else if (l[i] === 3) p++; } return { farm: f, ash: a, pasture: p }; });
  const capacity = () => t.evaluate(() => { const c = window.editor.doc.physics?.capacity; return c ? Array.from(c).reduce((s, v) => s + v, 0) : null; });
  const stroke = async (x0, y0, x1, y1, steps = 8) => { const m = t.page.mouse; await m.move(x0, y0); await m.down(); await m.move(x1, y1, { steps }); await m.up(); };

  const h0 = await biomeHash(), cap0 = await capacity();
  t.check('capacity is computed at generate time', cap0 !== null && cap0 > 0, cap0);

  await t.evaluate(() => { window.editor.setTool('landuse'); window.editor.setOptions({ radius: 30, strength: 0.2, landUse: 1 }); });
  await stroke(land.sx - 30, land.sy, land.sx + 30, land.sy + 5);
  const farm = await counts();
  t.check('a farmland stroke paints farmland', farm.farm > 0 && farm.ash === 0, farm);
  t.check('and leaves the biome untouched', (await biomeHash()) === h0);

  await t.evaluate(() => window.editor.setOptions({ landUse: 2 }));
  await stroke(land.sx, land.sy + 25, land.sx + 40, land.sy + 30);
  const both = await counts();
  t.check('an ashlands stroke adds ashlands', both.ash > 0 && both.farm > 0, both);

  await t.evaluate(() => window.editor.undoLast());
  const undone = await counts();
  t.check('undo removes the ashlands stroke only', undone.ash === 0 && undone.farm === farm.farm, undone);
  await t.evaluate(() => window.editor.redoLast());
  t.check('redo restores it', JSON.stringify(await counts()) === JSON.stringify(both));

  await t.evaluate(() => window.editor.applyPhysics('sculpt'));
  t.check('applying physics does not touch land use', JSON.stringify(await counts()) === JSON.stringify(both));
  await t.evaluate(() => window.editor.applyPhysics('revert'));
  t.check('reverting does not touch it either, and the biomes are back', JSON.stringify(await counts()) === JSON.stringify(both) && (await biomeHash()) === h0);
  t.check('land use does not move capacity', (await capacity()) === cap0, { cap0, now: await capacity() });

  await t.evaluate(() => window.editor.setOptions({ landUse: 3, radius: 40 }));
  await stroke(land.sx - 40, land.sy - 40, land.sx + 40, land.sy - 35, 10);
  await t.evaluate(() => window.editor.applyPhysics('sculpt'));
  const pins = await t.evaluate(() => { let n = 0; for (const [, e] of window.editor.doc.physics.edits) if (e.lu === 3) n++; return n; });
  t.check('applying pins painted pasture tiles into the physics edits', pins > 0, pins);

  const rt = await t.evaluate(async () => {
    const { saveDocument, loadDocument } = await import('/src/engine/doc/serialize.ts');
    const back = await loadDocument(await saveDocument(window.editor.doc));
    const a = window.editor.doc.physics.capacity, b = back.physics?.capacity;
    return !!b && a.length === b.length && a.every((v, i) => v === b[i]);
  });
  t.check('capacity round-trips through .wmap', rt);
});
