'use strict';
// The history expansion end to end: the wizard offers its features, a world generated with all of them carries persons,
// battles and route states, a person card opens from a chronicle line, and the battle overlay draws at the scrubbed turn.
const { run } = require('./harness.cjs');

/** A cheap fingerprint of the overlay canvas (the 2D layer above the map): changes when anything is drawn on it. */
const overlayHash = (t) => t.evaluate(() => {
  const c = [...document.querySelectorAll('canvas')].find((x) => x.getContext('2d', { willReadFrequently: true }));
  const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
  let h = 0x811c9dc5, lit = 0;
  for (let i = 3; i < d.length; i += 4 * 7) { h = Math.imul(h ^ d[i] ^ (d[i - 1] << 8), 16777619); if (d[i]) lit++; }
  return { h: h >>> 0, lit };
});

run('history expansion: persons, battles, overlay', async (t) => {
  const page = t.page;

  // The wizard lists the engine's expansion features on the Realms step.
  await page.click('text=New world wizard…');
  await page.click('nav[aria-label=Steps] >> text=Realms');
  const offered = await page.$$eval('[data-testid=history-features] label', (ls) => ls.map((l) => l.textContent.trim()));
  t.check('the wizard offers the expansion features', ['Persons', 'Battles', 'Sieges', 'Logistics', 'Commerce', 'Outlaws', 'Intrigue'].every((f) => offered.includes(f)), offered);
  await page.click('[data-testid=history-features] >> text=Battles');
  const ticked = await page.$$eval('[data-testid=history-features] input', (is) => is.map((i) => i.checked));
  t.check('ticking one feature ticks only that one (the rest start off, not at the engine default)', ticked.filter(Boolean).length === 1, ticked);
  await t.shot('history-expansion-wizard.png');
  await page.keyboard.press('Escape');

  // Every feature on: 127 = Persons | Battles | Sieges | Logistics | Commerce | Outlaws | Intrigue.
  const history = { enabled: true, turns: 30, years: 10, maxPolities: 8, roster: [], founders: [], features: 127 };
  await t.generate({ seed: 42, size: 3, history });
  const x = await t.evaluate(() => {
    const h = window.editor.doc.history, e = h && h.expansion;
    if (!e) return null;
    return {
      persons: e.persons.length / 25, named: e.personNames.filter(Boolean).length, battles: e.battles.length / 24, routes: e.routeStates.length / 10,
      samples: e.routeSeries.length / 5, withParts: h.events.filter((ev) => ev.parts && ev.parts.some((p) => p.role.startsWith('person:'))).length,
      battleTurns: [...new Set(Array.from({ length: e.battles.length / 24 }, (_, i) => e.battles[i * 24 + 1]))],
      // The first field battle (kind 0): its turn and tile, to scrub to and look at.
      field: (() => { for (let i = 0; i < e.battles.length / 24; i++) if (e.battles[i * 24 + 4] === 0) return { turn: e.battles[i * 24 + 1], tile: e.battles[i * 24 + 5] }; return null; })(),
      chronicle: !!e.chronicle,
    };
  });
  t.check('a world with every feature carries expansion records', !!x && x.persons > 0 && x.battles > 0 && x.routes > 0 && x.samples > 0, x);
  t.check('every person is named', !!x && x.named === x.persons, x && [x.named, x.persons]);
  t.check('events name their persons through person: participants', !!x && x.withParts > 0, x);
  t.note('expansion', x);

  // History tab: the chronicle lists events with links to their persons.
  await page.click('#tab-history');
  await page.waitForSelector('[data-testid=event-person]', { timeout: 10000 }).catch(() => {});
  // Persons rarely show among major events only: widen the log first.
  const major = await page.$('text=Only major events');
  if (major && (await page.$$('[data-testid=event-person]')).length === 0) await major.click();
  const links = await page.$$('[data-testid=event-person]');
  t.check('the chronicle links events to persons', links.length > 0, links.length);
  if (links.length) {
    const who = (await links[0].textContent()).trim();
    await links[0].click();
    await page.waitForSelector('[data-testid=person-card]', { timeout: 5000 });
    const title = (await page.textContent('#person-title')).trim();
    t.check('a person card opens from an event, titled with that person\'s name', title === who && title.length > 0 && !/^Person \d+$/.test(title), [who, title]);
    const card = await page.textContent('[data-testid=person-card]');
    t.check('the card shows a lifespan and the person\'s figures', /Born turn \d+/.test(card) && /Martial/.test(card), card.slice(0, 200));
    await t.shot('history-expansion-person-card.png');
    const bond = await page.$('[data-testid=person-card] [data-testid=person-link]');
    if (bond) {
      const other = (await bond.textContent()).trim();
      await bond.click();
      const next = (await page.textContent('#person-title')).trim();
      t.check('a bond opens the other person\'s card, with a way back', next === other && !!(await page.$('[data-testid=person-card] [aria-label=Back]')), [other, next]);
    } else t.note('bonds', 'the first person has none');
    await page.click('[data-testid=person-card] [aria-label=Close]');
    t.check('the card closes', !(await page.$('[data-testid=person-card]')));
  }

  // The battle overlay: scrub to a turn with battles and toggle it.
  if (x && x.field) {
    await t.evaluate((tile) => { const xy = window.editor.doc.world.tileXY; window.editor.focus(xy[tile * 2], xy[tile * 2 + 1]); }, x.field.tile);
    await t.setView({ historyTurn: x.field.turn, layers: { labels: false, realms: false, borders: false, battles: false, outlaws: false, routeSafety: false } });
    await page.waitForTimeout(300);
    const before = await overlayHash(t);
    await page.click('text=Show battles and sieges');
    await page.waitForTimeout(300);
    const on = await t.evaluate(() => window.editor.view().layers.battles);
    const after = await overlayHash(t);
    t.check('the toggle turns the battle layer on', on === true);
    t.check('battle markers draw at the scrubbed turn', after.h !== before.h && after.lit > before.lit, { before, after });
    await t.shot('history-expansion-battle.png');
    await page.click('text=Show battles and sieges');
    await page.waitForTimeout(300);
    t.check('the toggle turns the battle layer off again', (await t.evaluate(() => window.editor.view().layers.battles)) === false);
  }

  // Road safety: at the end of history, looking at a road whose route has samples.
  const road = await t.evaluate(() => {
    const e = window.editor.doc.history.expansion, s = e.routeSeries, r = e.roads;
    if (!s.length) return null;
    const a = s[1], b = s[2];
    for (let o = 0; o < r.length; o += 7) if (r[o + 3] === a && r[o + 4] === b) return r[o];
    return null;
  });
  if (road !== null) {
    await t.evaluate((tile) => { const xy = window.editor.doc.world.tileXY; window.editor.focus(xy[tile * 2], xy[tile * 2 + 1], 120); }, road);
    await t.setView({ historyTurn: -1, layers: { battles: false, outlaws: false, routeSafety: false } });
    await page.waitForTimeout(300);
    const before = await overlayHash(t);
    await page.click('text=Show road safety');
    await page.waitForTimeout(300);
    const after = await overlayHash(t);
    t.check('road safety colours the roads', after.lit > before.lit, { before, after });
    await page.click('text=Show outlaws and raids');
    await page.click('text=Show battles and sieges');
    await page.waitForTimeout(300);
    await t.shot('history-expansion-overlay.png');
  } else t.note('road safety', 'no sampled route to look at');

  // A world with every feature off keeps the record it always had: no expansion, no participants.
  await t.generate({ seed: 42, size: 3, history: { ...history, features: 0 } });
  const legacy = await t.evaluate(() => ({ expansion: 'expansion' in (window.editor.doc.history || {}), parts: window.editor.doc.history.events.some((e) => 'parts' in e) }));
  t.check('features off: the history record has no expansion and no participants', !legacy.expansion && !legacy.parts, legacy);

  // The expansion survives save and open.
  await t.generate({ seed: 42, size: 3, history });
  const trip = await t.evaluate(async () => {
    const a = window.editor.doc.history.expansion;
    const b = await window.editor.save(); await window.editor.open(b);
    const c = window.editor.doc.history.expansion;
    return !!c && c.persons.length === a.persons.length && c.personNames.join() === a.personNames.join() && c.routeSeries.length === a.routeSeries.length;
  });
  t.check('the expansion survives a .wmap save and open', trip);
});
