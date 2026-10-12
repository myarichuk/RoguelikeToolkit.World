'use strict';
// The "Calm belts" toggle (WindCellBlend) end to end: off keeps unit wind everywhere, on fades it to a calm at the cell
// edges, the setting survives Apply physics, and its tooltip is honest about what it costs.
const { run } = require('./harness.cjs');

const S = 10;   // editor climate stride; wind east/north are at 2 and 3 (world/climate.ts)
const DESERTS = [2, 12, 14, 15, 16];

run('Calm belts (wind cell blend)', async (t) => {
  const measure = async (blend) => {
    await t.page.evaluate((b) => window.editor.generate({ seed: 42, size: 4, plates: 12, styles: [], title: 'wind', model: { windCellBlend: b } }), blend);
    return t.evaluate(({ S, DESERTS }) => {
      const d = window.editor.doc, c = d.physics.climate, n = c.length / S, b = d.biome;
      let lo = Infinity, hi = 0, notUnit = 0, calm = 0, zero = [];
      for (let i = 0; i < n; i++) {
        const sp = Math.hypot(c[i * S + 2], c[i * S + 3]);
        if (sp < lo) lo = sp; if (sp > hi) hi = sp;
        if (Math.abs(sp - 1) > 0.01) notUnit++;
        if (sp < 0.15) calm++;
        if (sp === 0 && zero.length < 5) zero.push(i);
      }
      let desert = 0;
      for (let i = 0; i < b.length; i++) if (DESERTS.includes(b[i])) desert++;
      return { setting: d.settings.windCellBlend, lo, hi, n, notUnit, calm, zero, desert, hist: Array.from(b).reduce((h, x) => (h[x] = (h[x] || 0) + 1, h), {}) };
    }, { S, DESERTS });
  };

  const off = await measure(0);
  const on = await measure(1);
  t.note('wind speed range, off', [off.lo.toFixed(3), off.hi.toFixed(3)]);
  t.note('wind speed range, on', [on.lo.toFixed(3), on.hi.toFixed(3)]);
  t.note('desert-family pixels off/on', [off.desert, on.desert]);

  t.check('the setting reaches the document', off.setting === 0 && on.setting === 1, [off.setting, on.setting]);
  t.note('tiles off unit speed / calm (<0.15), off', [off.notUnit, off.calm, 'of', off.n, 'zero-wind tiles', off.zero]);
  t.note('tiles off unit speed / calm (<0.15), on', [on.notUnit, on.calm]);
  t.check('off keeps unit wind speed on all but a handful of tiles', off.hi < 1.01 && off.notUnit / off.n < 0.005, [off.notUnit, off.n]);
  t.check('off has no calm belts to speak of', off.calm / off.n < 0.005, [off.calm, off.n]);
  t.check('on fades the wind to a calm at the cell edges', on.calm / on.n > 0.02, [on.calm, on.n]);
  t.check('on still has full-strength wind mid-cell', on.hi > 0.9, on.hi);
  t.check('the toggle changes the world, not only the arrows', JSON.stringify(off.hist) !== JSON.stringify(on.hist));
  t.check('calm belts make slightly more desert (measured +10-40% in Core)', on.desert > off.desert, [off.desert, on.desert]);

  // Apply physics must carry the setting through, not reset it.
  const applied = await t.evaluate(async () => { await window.editor.applyPhysics('fix'); return window.editor.doc.settings.windCellBlend; });
  t.check('Apply physics keeps the setting', applied === 1, applied);

  // The slider's hint names the cost (fewer dunes), not just the benefit. Hints sit behind a real (i) button, so
  // read it the way a user does: open the Climate model section, then "About Calm belts", and take the text it reveals.
  const disclosure = t.page.getByRole('button', { name: 'Climate model', exact: true });
  if ((await disclosure.getAttribute('aria-expanded')) !== 'true') await disclosure.click();
  await t.page.getByRole('button', { name: 'About Calm belts' }).click();
  const tip = await t.page.getByText(/doldrums/).first().textContent({ timeout: 3000 }).catch(() => '');
  t.check('the Calm belts tooltip warns about wind erosion', /wind erosion/i.test(tip), tip.slice(0, 160));
});
