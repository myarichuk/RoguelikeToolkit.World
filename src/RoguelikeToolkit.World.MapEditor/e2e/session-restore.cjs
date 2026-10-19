'use strict';
// A reload picks up the map from the last visit instead of opening the wizard.
const { run } = require('./harness.cjs');

run('session autosave and restore', async (t) => {
  await t.generate({ seed: 77, size: 3, title: 'Restore me' });
  await t.evaluate(() => window.editor.saveSession());
  await t.page.reload();
  await t.page.waitForFunction(() => window.editor && window.editor.doc.settings.title === 'Restore me', null, { timeout: 60000 });
  const seed = await t.evaluate(() => window.editor.doc.settings.seed);
  t.check('the stored map is back after a reload', seed === 77, seed);
  t.check('the wizard stays closed when a map was restored', (await t.page.locator('[role=dialog]').count()) === 0);

  // The harness switches the wizard off on every load, so with nothing stored a fresh world is generated.
  await t.evaluate(() => window.editor.forgetSession());
  await t.page.reload();
  await t.page.waitForFunction(() => window.editor && window.editor.doc.features.size > 0, null, { timeout: 240000 });
  const title = await t.evaluate(() => window.editor.doc.settings.title);
  t.check('with nothing stored the map is not restored', title !== 'Restore me', title);
}, {});
