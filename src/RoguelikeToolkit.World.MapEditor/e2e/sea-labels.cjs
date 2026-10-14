'use strict';
// Sea and ocean names belong on water: every placed ocean/sea label must read mostly water
// along its baseline, on several seeds. A label sprawling over land is a placement bug.
const { run } = require('./harness.cjs');

run('sea and ocean labels stay off land', async (t) => {
  const worst = [];
  const seeds = (process.env.SEEDS || '1337,42,7,99').split(',').map(Number);
  for (const seed of seeds) {
    await t.generate({ seed, size: 5, plates: 14, title: 'seas' });
    const labels = await t.evaluate(() => {
      const ed = window.editor, d = ed.doc, theme = ed.theme();
      const out = [];
      for (const l of d.labels.all()) {
        if (l.style !== 'ocean' && l.style !== 'sea') continue;
        const r = ed.labels.resolve(l, theme);
        if (!r) continue;
        const b = r.geo.bounds;
        const step = Math.max(4, (l.size || r.style.size) / 2);
        let land = 0, total = 0;
        for (let y = b.y0; y <= b.y1 && total < 400; y += step) {
          for (let x = b.x0; x <= b.x1 && total < 400; x += step) {
            total++;
            if (d.elevationAt(x, y) >= 0) land++;
          }
        }
        out.push({ text: l.text, style: l.style, landFrac: total ? +(land / total).toFixed(2) : 0 });
      }
      return out;
    });
    const bad = labels.filter((l) => l.landFrac > 0.4);
    t.note(`seed ${seed} ocean/sea labels`, labels.map((l) => `${l.text}:${l.landFrac}`).join(', ') || 'none');
    t.check(`seed ${seed}: no ocean/sea label sits mostly on land`, bad.length === 0, bad);
    for (const l of labels) worst.push({ seed, ...l });
  }
  worst.sort((a, b) => b.landFrac - a.landFrac);
  t.note('worst land fractions', worst.slice(0, 5));
});
