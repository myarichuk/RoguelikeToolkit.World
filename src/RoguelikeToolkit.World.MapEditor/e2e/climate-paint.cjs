'use strict';
// The climate brush: a strong cold patch must reach the engine, survive save/open, be undoable, and revert cleanly.
const { run } = require('./harness.cjs');

run('climate brush through the engine', async (t) => {
  await t.generate({ seed: 42, size: 4 });

  // A broad inland land patch at mid latitude, with the camera centred on it.
  const spot = await t.evaluate(() => {
    const d = window.editor.doc, W = d.width, H = d.height;
    for (let y = Math.floor(H * 0.3); y < H * 0.45; y += 8) for (let x = 0; x < W; x += 8) {
      let ok = true;
      for (let dy = -40; dy <= 40 && ok; dy += 10) for (let dx = -40; dx <= 40; dx += 10) if (d.elevationAt(x + dx, y + dy) < 0.08) { ok = false; break; }
      if (ok) return { x, y };
    }
    return null;
  });
  t.check('the world has a broad inland land patch to paint on', !!spot, spot);
  if (!spot) return;

  await t.evaluate((s) => { window.__s = s; const c = window.editor.camera; c.cx = s.x; c.cy = s.y; c.zoom = 3; c.constrain(); window.editor.requestRender(); }, spot);
  const biomes = () => t.evaluate(() => { const d = window.editor.doc, h = {}, s = window.__s; for (let dy = -60; dy <= 60; dy += 2) for (let dx = -60; dx <= 60; dx += 2) { const b = d.biomeAt(s.x + dx, s.y + dy); h[b] = (h[b] || 0) + 1; } return JSON.stringify(h); });
  // `mass` (sum of |offset|) is what an undo changes: overlapping strokes cover the same texels, so the count of
  // non-zero texels stays put when the last stroke is undone.
  const painted = () => t.evaluate(() => { const d = window.editor.doc; let n = 0, mn = 0, mass = 0; for (let i = 0; i < d.warmth.length; i++) if (d.warmth[i] !== 0) { n++; mn = Math.min(mn, d.warmth[i]); mass += Math.abs(d.warmth[i]); } return { texels: n, min: mn, mass, wet: Array.from(d.wetness).some((v) => v !== 0) }; });
  const before = await biomes();

  await t.evaluate(() => { window.editor.setTool('climate'); window.editor.setOptions({ radius: 90, strength: 0.2, climateLayer: 'temperature', climateMode: 'lower' }); });
  const box = await t.canvasBox(), cx = box.x + box.w / 2, cy = box.y + box.h / 2;
  const m = t.page.mouse;
  for (let pass = 0; pass < 6; pass++) { await m.move(cx - 60, cy - 60); await m.down(); for (let k = 0; k <= 12; k++) await m.move(cx - 60 + k * 10, cy - 60 + (k % 2) * 120); await m.up(); }
  const p = await painted();
  t.check('the brush paints a strong cold patch', p.texels > 0 && p.min <= -60, p);
  t.check('and only on the temperature layer', !p.wet, p);

  await t.evaluate(() => window.editor.undoLast());
  const afterUndo = (await painted()).mass;
  t.check('one undo removes one stroke but not all of them', afterUndo < p.mass && afterUndo > 0, { before: p.mass, afterUndo });
  await t.evaluate(() => window.editor.redoLast());
  t.check('redo restores it exactly', (await painted()).mass === p.mass);

  await t.evaluate(() => window.editor.applyPhysics('sculpt'));
  const after = await biomes();
  t.check('applying a large cold patch changes the biomes around it', after !== before);

  const kept = await t.evaluate(async () => {
    const b = await window.editor.save(); await window.editor.open(b);
    const d = window.editor.doc; let n = 0; for (let i = 0; i < d.warmth.length; i++) if (d.warmth[i] !== 0) n++;
    return { texels: n, edits: [...d.physics.edits.values()].filter((e) => e.dt !== undefined).length };
  });
  t.check('paint and its per-tile offsets survive save/open', kept.texels > 0 && kept.edits > 0, kept);

  const idem = await t.evaluate(async () => { const a = Array.from(window.editor.doc.biome.slice(0, 200000)); await window.editor.applyPhysics('sculpt'); const b = Array.from(window.editor.doc.biome.slice(0, 200000)); return a.every((v, i) => v === b[i]); });
  t.check('applying twice is idempotent', idem);

  await t.evaluate(() => window.editor.applyPhysics('revert'));
  t.check('revert clears the paint', !(await t.evaluate(() => window.editor.doc.hasClimatePaint)));
  t.check('and returns the biomes to the original', (await biomes()) === before);
  await t.evaluate(() => window.editor.undoLast());
  t.check('undoing the revert brings the paint back', await t.evaluate(() => window.editor.doc.hasClimatePaint));
});
