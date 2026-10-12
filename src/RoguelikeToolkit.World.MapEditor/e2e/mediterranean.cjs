'use strict';
// The Mediterranean biome end to end: the engine makes it from winter-wet rain, the editor draws, names and offers it.
const { run } = require('./harness.cjs');

const MEDITERRANEAN = 19, SAVANNA = 10, S = 10;
// Core BiomeClassifier.IsMediterranean: phase >= 0.60, temperature >= 0.50, moisture <= 0.55.
const MIN_PHASE = 0.6, MIN_TEMP = 0.5, MAX_MOISTURE = 0.55;

run('Mediterranean biome', async (t) => {
  await t.generate({ seed: 7, size: 4 });

  const r = await t.evaluate(({ S, MED, SAV, P, T, M }) => {
    const d = window.editor.doc, c = d.physics.climate, W = d.width, H = d.height;
    const ok = (tile) => c[tile * S + 9] >= P && c[tile * S + 0] >= T && c[tile * S + 1] <= M;
    let land = 0, med = 0, fit = 0, interior = null, sav = 0;
    for (let y = 8; y < H - 8; y++) for (let x = 8; x < W - 8; x++) {
      if (d.elevationAt(x, y) < 0) continue;
      land++;
      const b = d.biome[y * W + x];
      if (b === SAV) sav++;
      if (b !== MED) continue;
      med++;
      const tile = d.tileAt(x, y);
      if (ok(tile)) {
        fit++;
        if (!interior && [[-6, 0], [6, 0], [0, -6], [0, 6], [-6, -6], [6, 6], [-6, 6], [6, -6]].every(([dx, dy]) => d.biome[(y + dy) * W + x + dx] === MED)) interior = { x, y };
      }
    }
    return { land, med, fit, sav, interior };
  }, { S, MED: MEDITERRANEAN, SAV: SAVANNA, P: MIN_PHASE, T: MIN_TEMP, M: MAX_MOISTURE });

  const share = (100 * r.med) / r.land;
  t.note('Mediterranean share of land pixels', `${share.toFixed(2)}%`);
  t.check('Mediterranean covers a small share of land (Earth is about 2%)', share > 0.3 && share < 6, share);
  // Pixels are blended across tile borders (edge pixels carry a neighbour's biome), so demand most, not all.
  t.check('most Mediterranean pixels sit on tiles that satisfy the rule', r.med > 0 && r.fit / r.med > 0.8, { med: r.med, fit: r.fit });
  t.check('savanna still exists alongside it', r.sav > 0, r.sav);

  if (r.interior) {
    await t.setView({ layers: { climate: true, wind: false, currents: false }, climateKind: 'rainSeason' });
    await t.evaluate((p) => window.editor.pointer(p.x, p.y), r.interior);
    await t.page.waitForTimeout(500);
    const status = await t.statusBar();
    t.check('the status bar names the biome over an interior pixel', /Mediterranean/i.test(status), status.slice(0, 200));
    t.check('and says the rain falls in winter', /rain in winter/i.test(status), status.slice(0, 200));
  } else {
    t.check('an interior Mediterranean pixel exists to inspect', false);
  }

  // The Biome tool offers it, and never offers the land-use colour slots as biomes.
  await t.page.click('button:has-text("Biome")');
  await t.page.waitForSelector('.swatches span');
  const names = await t.page.$$eval('.swatches span', (e) => e.map((x) => x.textContent));
  t.check('the biome palette lists Mediterranean', names.includes('Mediterranean'), names);
  t.check('and does not list the land-use slots', !['Farmland', 'Ashlands', 'Pasture', 'Built'].some((n) => names.includes(n)), names);
  await t.page.click('.swatches button:has-text("Mediterranean")');
  t.check('selecting its swatch selects biome 19', (await t.evaluate(() => window.editor.options?.biome)) === MEDITERRANEAN);
  const tip = await t.page.getAttribute('.swatches button:has-text("Mediterranean")', 'title');
  t.check('its tooltip explains the winter rain in plain words', /winter/i.test(tip || ''), tip);
});
