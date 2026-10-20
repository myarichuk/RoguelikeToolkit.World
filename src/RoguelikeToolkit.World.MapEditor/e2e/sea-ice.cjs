'use strict';
// Sea ice end to end: Core classifies it per tile, the editor shows it in the status bar only while the overlay is on,
// and the settlement export (which now treats pack ice as no water access) still runs and is repeatable.
const { run } = require('./harness.cjs');

// Sites are only scored for worlds without history; with it, history founds the settlements instead.
const NO_HISTORY = { enabled: false, turns: 30, years: 10, maxPolities: 8, roster: [], founders: [] };
const OPEN = 0, SEASONAL = 1, PERENNIAL = 2;

run('Sea ice', async (t) => {
  await t.generate({ seed: 7, size: 4, history: NO_HISTORY });

  const spots = await t.evaluate(() => {
    const d = window.editor.doc, s = d.physics.seaIce, out = {}, counts = [0, 0, 0];
    for (let i = 0; i < s.length; i++) counts[s[i]] = (counts[s[i]] || 0) + 1;
    for (let y = 0; y < d.height; y += 2) for (let x = 0; x < d.width; x += 2) {
      const k = s[d.tileAt(x, y)];
      if (d.elevationAt(x, y) < 0 && out[k] === undefined) out[k] = { x, y };
    }
    return { out, counts, values: [...new Set(s)].sort() };
  });
  t.note('tiles per kind (open/land, seasonal, perennial)', spots.counts);
  t.check('the engine reports only the three ice kinds', spots.values.every((v) => v === OPEN || v === SEASONAL || v === PERENNIAL), spots.values);
  // Positive controls: without a spot of each kind the readout assertions below would pass vacuously.
  t.check('there are seasonal-ice tiles to inspect', spots.counts[SEASONAL] > 0 && !!spots.out[SEASONAL], spots.counts);
  t.check('there are perennial-ice (pack) tiles to inspect', spots.counts[PERENNIAL] > 0 && !!spots.out[PERENNIAL], spots.counts);

  const read = async (overlay, p) => {
    await t.setView({ layers: { climate: overlay, wind: false, currents: false }, climateKind: 'seaIce' });
    await t.evaluate((q) => window.editor.pointer(q.x, q.y), p);
    await t.page.waitForTimeout(400);
    return t.statusBar();
  };
  if (spots.out[OPEN]) t.check('open sea says nothing about ice', !/sea ice/.test(await read(true, spots.out[OPEN])));
  if (spots.out[SEASONAL]) t.check('seasonal ice is named "in winter"', /sea ice in winter/.test(await read(true, spots.out[SEASONAL])));
  if (spots.out[PERENNIAL]) {
    t.check('pack ice is named "all year"', /sea ice all year/.test(await read(true, spots.out[PERENNIAL])));
    const off = await read(false, spots.out[PERENNIAL]);
    t.check('with the overlay off the climate readout is not shown', !/temp|sea ice/.test(off), off.slice(0, 200));
  }

  // Settlements: the export runs on a world with ice, places the tiers, and the same seed repeats exactly.
  const sites = () => t.evaluate(() => {
    const kinds = {}, key = [];
    for (const f of window.editor.doc.features.all()) {
      if (!['capital', 'city', 'town', 'village'].includes(f.kind)) continue;
      kinds[f.kind] = (kinds[f.kind] || 0) + 1;
      key.push(`${f.kind}@${Math.round(f.x)},${Math.round(f.y)}`);
    }
    return { kinds, key: key.sort().join('|') };
  });
  const first = await sites();
  t.note('settlements', first.kinds);
  t.check('every settlement tier is placed', ['capital', 'city', 'town', 'village'].every((k) => first.kinds[k] > 0), first.kinds);
  await t.generate({ seed: 7, size: 4, history: NO_HISTORY });
  t.check('the same seed places the same settlements', (await sites()).key === first.key);

  // The export says why each site scored what it did, and the editor shows it as a fact on the settlement.
  const why = await t.evaluate(() => {
    const out = [];
    for (const f of window.editor.doc.features.all()) {
      if (!['capital', 'city', 'town', 'village'].includes(f.kind)) continue;
      const row = f.details.find((d) => d[0] === 'Site factors');
      out.push({ kind: f.kind, factors: row ? row[1] : null, score: Number(f.details.find((d) => d[0] === 'Site score')[1]) });
    }
    return out;
  });
  const known = /^(fresh water|fertile land|harsh terrain|sea ice in winter|high ground|shaped by history|past events|river|fishing)(, (fresh water|fertile land|harsh terrain|sea ice in winter|high ground|shaped by history|past events|river|fishing))*$/;
  t.check('settlements list their site factors in plain words', why.length > 0 && why.every((w) => w.factors && known.test(w.factors)), why.filter((w) => !w.factors || !known.test(w.factors)).slice(0, 3));
  const best = why.slice().sort((a, b) => b.score - a.score)[0];
  t.check('the best-scoring site is credited with fresh water', /fresh water/.test(best.factors), best);
});
