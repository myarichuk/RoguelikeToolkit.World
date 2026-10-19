'use strict';
// Shared plumbing for the browser checks. See README.md for how to run them.
const fs = require('fs');
const os = require('os');
const path = require('path');

function loadPlaywright() {
  const candidates = [process.env.PLAYWRIGHT_MODULE, 'playwright', '@playwright/test', '/opt/node-tools/node_modules/playwright'].filter(Boolean);
  for (const c of candidates) {
    try { return require(c); } catch { /* try the next */ }
  }
  throw new Error('Playwright not found. Install it (npm i --no-save playwright) or set PLAYWRIGHT_MODULE to its path.');
}

const URL = process.env.E2E_URL || 'http://localhost:5199/';
const OUT = process.env.E2E_OUT || path.join(os.tmpdir(), 'mapeditor-e2e');
const GENERATE_TIMEOUT_MS = 240000;   // first load compiles/starts the WASM engine; slow machines need minutes

/**
 * Runs `body(t)` against a freshly loaded editor and exits non-zero if any check failed, the body threw, or the
 * page produced an uncaught error / HTTP >= 400 (favicon aside). `t` carries the page and the helpers below.
 */
async function run(name, body, { viewport = { width: 1500, height: 900 } } = {}) {
  const { chromium } = loadPlaywright();
  fs.mkdirSync(OUT, { recursive: true });
  const launch = { args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] };
  if (process.env.E2E_CHROMIUM) launch.executablePath = process.env.E2E_CHROMIUM;
  else if (fs.existsSync('/opt/pw-browsers/chromium')) launch.executablePath = '/opt/pw-browsers/chromium';
  const browser = await chromium.launch(launch);
  const ctx = await browser.newContext({ viewport });
  const page = await ctx.newPage();
  // The new-world wizard opens at start; the suites expect a generated world, so they opt out of it.
  await page.addInitScript(() => { try { localStorage.setItem('wme.wizard', 'off'); } catch { /* storage unavailable */ } });

  const problems = [];
  let failed = 0;
  ctx.on('response', (r) => { if (r.status() >= 400 && !r.url().endsWith('favicon.ico')) problems.push(`HTTP ${r.status()} ${r.url()}`); });
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error' && !/favicon|Failed to load resource/.test(m.text())) problems.push(`console.error: ${m.text().slice(0, 200)}`); });

  const t = {
    page, OUT,
    /** Records one assertion; the detail is only printed on failure, or always when `show` is set. */
    check(label, ok, detail = '') {
      if (!ok) failed++;
      console.log(`${ok ? '  ok  ' : ' FAIL '} ${label}${!ok && detail !== '' ? `  [${typeof detail === 'string' ? detail : JSON.stringify(detail)}]` : ''}`);
    },
    note(label, value) { console.log(`  ..   ${label}: ${typeof value === 'string' ? value : JSON.stringify(value)}`); },
    evaluate: (fn, arg) => page.evaluate(fn, arg),
    async generate({ seed = 42, size = 3, plates = 12, title = 'e2e' } = {}) {
      await page.evaluate((o) => window.editor.generate({ ...o, styles: [] }), { seed, size, plates, title });
    },
    /** Bounding box of the map canvas in page pixels. */
    canvasBox: () => page.evaluate(() => { const r = document.querySelector('canvas:last-of-type').getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height }; }),
    /** A screen position over land, found with the editor's own camera and sampler; null if the view shows none. */
    async landOnScreen(box) {
      return page.evaluate(({ box }) => {
        const ed = window.editor, d = ed.doc;
        for (let gy = 0.3; gy < 0.8; gy += 0.05) for (let gx = 0.3; gx < 0.8; gx += 0.05) {
          const p = ed.camera.toMap(box.w * gx, box.h * gy);
          if (p && d.elevationAt(p.x, p.y) > 0.05) return { sx: box.x + box.w * gx, sy: box.y + box.h * gy };
        }
        return null;
      }, { box });
    },
    /** Merges a patch into the view settings (layers merged one level deep). */
    setView: (patch) => page.evaluate((p) => { const v = window.editor.view(); window.editor.setView({ ...v, ...p, layers: { ...v.layers, ...(p.layers || {}) } }); }, patch),
    statusBar: () => page.evaluate(() => document.querySelector('[data-testid=statusbar]')?.innerText.replace(/\s+/g, ' ') ?? ''),
    async shot(file, clip) { await page.screenshot({ path: path.join(OUT, file), ...(clip ? { clip } : {}) }); },
  };

  console.log(`\n== ${name}`);
  try {
    await page.goto(URL);
    await page.waitForFunction(() => window.editor && window.editor.doc.features.size > 0, null, { timeout: GENERATE_TIMEOUT_MS });
    await body(t);
  } catch (e) {
    failed++;
    console.log(` FAIL  threw: ${e && e.stack ? e.stack.split('\n').slice(0, 3).join(' | ') : e}`);
  }
  t.check('no uncaught page errors or failed requests', problems.length === 0, problems.slice(0, 5));
  await browser.close();
  console.log(failed ? `== ${name}: ${failed} FAILED` : `== ${name}: passed`);
  process.exit(failed ? 1 : 0);
}

module.exports = { run, URL, OUT };
