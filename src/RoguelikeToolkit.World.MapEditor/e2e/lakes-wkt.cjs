'use strict';
// Lakes must exist on the map, not only in the data: every lake feature (and every WKT lake row)
// covers at least one visible water pixel. A named lake with no water anywhere is a phantom.
const { run } = require('./harness.cjs');

run('lakes exist on the map and in WKT', async (t) => {
  await t.generate({ seed: 1337, size: 5, plates: 14, title: 'lakes' });

  const lakes = await t.evaluate(() => {
    const d = window.editor.doc;
    // Per-tile count of raster pixels that read as water.
    const wet = new Map();
    for (let i = 0; i < d.width * d.height; i++) {
      if (d.elevation[i] < 0) {
        const tl = d.world.tile[i];
        wet.set(tl, (wet.get(tl) || 0) + 1);
      }
    }
    const rows = [];
    for (const f of d.features.all()) {
      if (f.kind !== 'lake' || !f.tiles) continue;
      let w = 0;
      for (const tl of f.tiles) w += wet.get(tl) || 0;
      rows.push({ id: f.id, name: f.name, tiles: f.tiles.length, wetPixels: w });
    }
    return rows;
  });
  const phantom = lakes.filter((l) => l.wetPixels === 0);
  t.note('lake features', lakes.length);
  t.note('phantom lakes (no water pixel)', phantom.map((l) => `${l.name}:${l.tiles}`).join(', ') || 'none');
  t.check('every lake feature covers visible water', phantom.length === 0, phantom.slice(0, 8));

  const wkt = await t.evaluate(async () => window.editor.exportWkt().text());
  const lines = wkt.trim().split('\n');
  const lakeRows = lines.filter((l) => /,lake,/.test(l));
  const ids = new Set(lakes.map((l) => l.id));
  const orphanRows = lakeRows.filter((l) => ![...ids].some((id) => l.includes(id)));
  t.check('WKT lake rows match lake features one to one',
    lakeRows.length === lakes.length && orphanRows.length === 0,
    { rows: lakeRows.length, features: lakes.length, orphans: orphanRows.slice(0, 3) });
});
