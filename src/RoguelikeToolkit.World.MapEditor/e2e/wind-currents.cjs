'use strict';
// Wind and current arrows draw on the overlay: enabling each layer adds clearly more
// non-transparent overlay pixels, and the two glyphs differ (open chevron vs solid wedge).
const { run } = require('./harness.cjs');

run('wind and current arrows draw', async (t) => {
  await t.generate({ seed: 42, size: 4, plates: 12, title: 'flow' });
  await t.setView({ layers: { wind: false, currents: false } });
  await t.page.waitForTimeout(900);

  const overlayPixels = () => {
    const c = document.querySelectorAll('canvas')[1];
    const g = c.getContext('2d');
    const d = g.getImageData(0, 0, c.width, c.height).data;
    let n = 0;
    for (let i = 3; i < d.length; i += 4) if (d[i] > 12) n++;
    return n;
  };

  const none = await t.evaluate(overlayPixels);
  await t.setView({ layers: { wind: true } });
  await t.page.waitForTimeout(900);
  const wind = await t.evaluate(overlayPixels);
  await t.shot('flow-wind.png');
  await t.setView({ layers: { currents: true } });
  await t.page.waitForTimeout(900);
  const both = await t.evaluate(overlayPixels);
  await t.shot('flow-both.png');

  t.note('overlay pixels none/wind/both', [none, wind, both]);
  t.check('wind arrows paint the overlay', wind - none > 500, [wind, none]);
  t.check('current wedges paint on top of that', both - wind > 300, [both, wind]);
});
