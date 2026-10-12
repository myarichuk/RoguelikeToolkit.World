'use strict';
// The engine's climate array reaches the editor intact, every layer renders, the readout works, and it round-trips.
const { run } = require('./harness.cjs');

const STRIDE = 10;   // climate.ts CLIMATE_STRIDE; keep in step with it

run('climate data and layers', async (t) => {
  await t.generate({ seed: 42, size: 4 });

  const st = await t.evaluate((S) => {
    const d = window.editor.doc, c = d.physics.climate, n = d.world.tileXY.length / 2;
    let nan = 0, phaseMin = Infinity, phaseMax = -Infinity, seasonMax = 0;
    for (let i = 0; i < c.length; i++) if (!Number.isFinite(c[i])) nan++;
    for (let tile = 0; tile < n; tile++) { const ph = c[tile * S + 9]; phaseMin = Math.min(phaseMin, ph); phaseMax = Math.max(phaseMax, ph); seasonMax = Math.max(seasonMax, c[tile * S + 8]); }
    return { len: c.length, tiles: n, nan, phaseMin, phaseMax, seasonMax };
  }, STRIDE);
  t.check('climate has the editor stride per tile', st.len === st.tiles * STRIDE, st);
  t.check('and no NaN or infinity', st.nan === 0, st);
  t.check('rain phase is signed and bounded in [-1, 1]', st.phaseMin >= -1 - 1e-6 && st.phaseMax <= 1 + 1e-6 && st.phaseMin < 0 && st.phaseMax > 0, st);

  // The current-speed tint is calibrated for "1 = a strong boundary current": the engine must really span that range,
  // and the climate array must carry a flow on the sea and none on land (the tint leaves still water clear).
  const flow = await t.evaluate((S) => {
    const d = window.editor.doc, c = d.physics.climate, sp = [];
    for (let i = 0; i < c.length / S; i++) sp.push(Math.hypot(c[i * S + 4], c[i * S + 5]));
    sp.sort((a, b) => a - b);
    return { p50: sp[Math.floor(sp.length * 0.5)], p99: sp[Math.floor(sp.length * 0.99)], moving: sp.filter((v) => v > 0.02).length / sp.length };
  }, STRIDE);
  t.note('current speed over all tiles', flow);
  t.check('currents span the tint range: the fastest 1% of tiles flow at about 1 or more, and only about the sea (not land) moves', flow.p99 > 0.8 && flow.p99 < 4 && flow.moving > 0.2 && flow.moving < 0.95, flow);

  // Each layer must actually paint something different from the plain map.
  const box = await t.canvasBox();
  const clip = { x: box.x, y: box.y, width: box.w, height: box.h };
  const pixels = async () => (await t.page.screenshot({ clip })).toString('base64');
  await t.setView({ layers: { climate: false, wind: false, currents: false } });
  await t.page.waitForTimeout(500);
  const plain = await pixels();
  for (const kind of ['temperature', 'rainfall', 'oceanHeat', 'currentSpeed', 'seasons', 'rainSeason']) {
    await t.setView({ climateKind: kind, layers: { climate: true } });
    await t.page.waitForTimeout(500);
    t.check(`the ${kind} layer changes the picture`, (await pixels()) !== plain);
  }
  await t.setView({ climateKind: 'rainSeason', layers: { climate: true, wind: true, currents: true } });
  await t.page.waitForTimeout(500);
  await t.shot('climate-layers.png', clip);

  const land = await t.landOnScreen(box);
  if (land) {
    await t.page.mouse.move(land.sx, land.sy);
    await t.page.waitForTimeout(400);
    const status = await t.statusBar();
    t.check('hovering land shows a climate readout', /°|temp|rain/i.test(status), status.slice(0, 200));
  }

  // Apply keeps the climate array consistent with the world; undo restores it.
  const sum = () => t.evaluate((S) => { const c = window.editor.doc.physics.climate; let s = 0; for (let i = 0; i < c.length; i += S) s += c[i]; return +s.toFixed(4); }, STRIDE);
  await t.evaluate(() => { window.editor.setTool('climate'); window.editor.setOptions({ radius: 60, strength: 1, climateKind: 'temperature', climateSign: 1 }); });
  if (land) { const m = t.page.mouse; await m.move(land.sx - 30, land.sy); await m.down(); await m.move(land.sx + 30, land.sy + 3, { steps: 8 }); await m.up(); }
  const s0 = await sum();
  await t.evaluate(() => window.editor.applyPhysics('sculpt'));
  const s1 = await sum();
  t.check('painting warmth and applying changes the temperature field', s1 !== s0, { s0, s1 });
  await t.evaluate(() => window.editor.undoLast());
  t.check('undo restores it exactly', (await sum()) === s0);

  const rt = await t.evaluate(async () => {
    const m = await import('/src/engine/doc/serialize.ts');
    const a = window.editor.doc.physics.climate, back = await m.loadDocument(await m.saveDocument(window.editor.doc)), b = back.physics?.climate;
    return !!b && a.length === b.length && a.every((v, i) => v === b[i]);
  });
  t.check('climate round-trips through .wmap', rt);
});
