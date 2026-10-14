'use strict';
// Rivers are drawn and visible: toggling the rivers layer repaints pixels at fit view (hundreds
// at full contrast), and zoomed in the ribbons are unmistakable.
const { run } = require('./harness.cjs');

const readGl = () => {
  const gl = document.querySelector('canvas');
  const g = gl.getContext('webgl2');
  const px = new Uint8Array(gl.width * gl.height * 4);
  g.readPixels(0, 0, gl.width, gl.height, g.RGBA, g.UNSIGNED_BYTE, px);
  return { w: gl.width, h: gl.height, px };
};

const riverColor = () => {
  const hex = window.editor.theme().map.river;
  return [parseInt(hex.slice(1, 3), 16), parseInt(hex.slice(3, 5), 16), parseInt(hex.slice(5, 7), 16)];
};

run('rivers are visible on the map', async (t) => {
  await t.generate({ seed: 1337, size: 5, plates: 14, title: 'rivers' });
  await t.setView({ layers: { rivers: true } });
  await t.page.waitForTimeout(900);

  const fit = await t.evaluate(({ readGl, riverColor }) => {
    const rgb = eval(`(${riverColor})`)();
    const a = eval(`(${readGl})`)();
    let near = 0;
    for (let i = 0; i < a.w * a.h; i++) {
      const dr = a.px[i * 4] - rgb[0], dg = a.px[i * 4 + 1] - rgb[1], db = a.px[i * 4 + 2] - rgb[2];
      if (dr * dr + dg * dg + db * db < 30 * 30) near++;
    }
    const rivers = [...window.editor.doc.rivers.all()].map((r) => r.points.length);
    return { near, w: a.w, h: a.h, riverEntities: rivers.length, buf: Array.from(a.px) };
  }, { readGl: readGl.toString(), riverColor: riverColor.toString() });

  await t.setView({ layers: { rivers: false } });
  await t.page.waitForTimeout(900);
  const off = await t.evaluate(({ readGl }) => {
    const a = eval(`(${readGl})`)();
    return { buf: Array.from(a.px) };
  }, { readGl: readGl.toString() });

  let changed = 0, strong = 0, bold = 0;

  for (let i = 0; i < fit.w * fit.h; i++) {
    const d =
      Math.abs(fit.buf[i * 4] - off.buf[i * 4]) +
      Math.abs(fit.buf[i * 4 + 1] - off.buf[i * 4 + 1]) +
      Math.abs(fit.buf[i * 4 + 2] - off.buf[i * 4 + 2]);
    if (d > 36) changed++;
    if (d > 90) strong++;
    if (d > 150) bold++;


  }
  t.note('river entities', fit.riverEntities);
  t.note('pixels changed by the rivers layer (any/strong/bold)', [changed, strong, bold]);
  await t.setView({ layers: { rivers: true } });
  await t.page.waitForTimeout(600);
  await t.shot('rivers-fit.png');
  t.check('the document has rivers', fit.riverEntities > 5, fit.riverEntities);
  t.check('the rivers layer repaints a visible share of pixels', changed > 500, changed);
  t.check('hundreds of repaints read at full contrast', strong > 300, strong);

  await t.setView({ layers: { rivers: true } });

  // Zoomed onto a long river the ribbons must be unmistakable.
  const zoomed = await t.evaluate(() => {
    const ed = window.editor, doc = ed.doc;
    let best = null;
    for (const r of doc.rivers.all()) {
      if (!best || r.points.length > best.points.length) best = r;
    }
    const n = best.points.length / 3, m = Math.floor(n / 2);
    ed.camera.cx = best.points[m * 3]; ed.camera.cy = best.points[m * 3 + 1]; ed.camera.zoom = 6;
    ed.requestRender();
    return { cx: ed.camera.cx, cy: ed.camera.cy };
  });
  await t.page.waitForTimeout(900);
  const z = await t.evaluate(({ readGl, riverColor }) => {
    const rgb = eval(`(${riverColor})`)();
    const a = eval(`(${readGl})`)();
    let near = 0;
    for (let i = 0; i < a.w * a.h; i++) {
      const dr = a.px[i * 4] - rgb[0], dg = a.px[i * 4 + 1] - rgb[1], db = a.px[i * 4 + 2] - rgb[2];
      if (dr * dr + dg * dg + db * db < 14 * 14) near++;
    }
    return { near };
  }, { readGl: readGl.toString(), riverColor: riverColor.toString() });
  t.note('zoomed river pixels (tight)', z.near);
  t.check('a zoomed river ribbon is unmistakable', z.near > 800, z.near);
  await t.shot('rivers-zoomed.png');
});
