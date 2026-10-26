# Browser checks

End-to-end checks that drive the real editor in headless Chromium against the real WebAssembly engine. They cover what
`npm test` (vitest, no browser) cannot: that Core's output reaches the editor intact, that the brushes, undo/redo and
`.wmap` save/open work through the actual UI, and that the engine and the TypeScript side agree on the wire format.

Each file is one flow. It asserts (`ok` / `FAIL` lines) and exits non-zero on any failure, on an uncaught page error,
or on an HTTP error from the page.

| Check | What it pins |
|---|---|
| `generate-save-open.cjs` | A generated world has tiles and features; the same seed gives the same world, another seed a different one; save → open restores it exactly. |
| `climate-paint.cjs` | The climate brush paints through real pointer events, undo/redo are exact, apply changes the biomes, save/open keeps the paint, apply is idempotent, revert restores the original. |
| `landuse.cjs` | Land use never changes the biome, apply/revert never touch it, it never moves capacity, pasture pins reach the physics edits, capacity round-trips through `.wmap`. |
| `climate-layers.cjs` | The engine's climate array has the editor stride, no NaN, a signed rain phase; each climate layer (including current speed) changes the picture, and the currents span the tint's calibrated range; the hover readout; apply/undo; round trip. |
| `mediterranean.cjs` | The Mediterranean biome covers a plausible share of land, sits on tiles that satisfy Core's rule, is named in the status bar, is offered (and the land-use slots are not) in the Biome palette, and has its tooltip. |
| `sea-ice.cjs` | The engine reports seasonal and perennial ice, the status bar names each ("in winter" / "all year") only while the overlay is on, and the settlement export places every tier, repeats exactly for the same seed, and gives each site its factors in plain words. |
| `wind-blend.cjs` | Calm belts off keeps unit wind everywhere, on fades it to a calm at the cell edges, changes the biomes (slightly more desert), survives Apply physics, and the tooltip warns that wind erosion builds fewer dunes. |
| `history-expansion.cjs` | The wizard offers the expansion features; a world with all of them carries named persons, battles and route states and events with `person:` participants; a person card opens from a chronicle line and follows a bond; the battle and road-safety layers draw at the scrubbed turn; features off leaves the history record as it was; the expansion survives save/open. |

## Running

You need the engine published into `public/_framework` (`./build.sh --engine`), a dev server, and Playwright.

```bash
npm install
./build.sh --engine                  # once, and after Core changes: the checks run the wasm, not the C# tests
npx vite --port 5199 --strictPort &  # or point E2E_URL at any running build
npm i --no-save playwright           # not a project dependency: it would add a browser download to every install

npm run e2e                          # all checks, one browser each
npm run e2e -- climate               # only files whose name contains "climate"
node e2e/mediterranean.cjs           # one check directly
```

The first load starts the engine and can take a minute or more on a slow machine (the wait is 4 minutes).

| Variable | Default | Meaning |
|---|---|---|
| `E2E_URL` | `http://localhost:5199/` | The editor to test. |
| `E2E_OUT` | `<tmp>/mapeditor-e2e` | Where screenshots go. |
| `E2E_CHROMIUM` | `/opt/pw-browsers/chromium` if present, else Playwright's own | Chromium executable. |
| `PLAYWRIGHT_MODULE` | resolved from `node_modules` | Path to a Playwright install, if it is not resolvable. |

Rendering uses SwiftShader (software WebGL2), so no GPU is needed.

## Writing one

Use `run(name, async (t) => { ... })` from `harness.cjs`. `t.check(label, ok, detail)` records an assertion (the detail is
printed on failure), `t.generate`, `t.landOnScreen`, `t.setView`, `t.statusBar` and `t.shot` cover the common moves, and
`t.page` is the Playwright page. Code passed to `t.evaluate` runs **in the browser**: it cannot see Node-side constants,
so pass them as the second argument.

Prefer measuring what the thing under test actually changes. Counting non-zero texels cannot tell a working undo from a broken
one when strokes overlap, while summing their magnitude can.

A check that has never failed has not been shown to check anything. When you add one, break the product in the way it should
catch (rename a biome in the pack, change a stride) and confirm it goes red.
