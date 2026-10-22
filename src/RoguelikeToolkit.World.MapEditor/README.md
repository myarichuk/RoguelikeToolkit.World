# World Map Editor

A browser map editor in the style of Inkarnate, built on `RoguelikeToolkit.World.Core`:

- **Generate:** Core generates the planet in the browser (.NET → WebAssembly, in a worker).
- **Draw:** the editor turns the planet into a hand-drawn map.
- **Edit:** you sculpt terrain, paint biomes, place stamps, write labels and draw rivers.
- **Export:** vector SVG, PNG, and WKT (a CSV of rivers, towns, deposits and area anchors in lon/lat, for GIS tools).

![editor](docs/editor-map.jpg)

| Plates + topography view | Inspecting a mountain range |
|---|---|
| ![plates](docs/editor-plates-topo.jpg) | ![inspect](docs/editor-inspect.jpg) |

| Theme from a content pack | SVG export (vector coast, rings, symbols, glyph-outline labels) |
|---|---|
| ![theme](docs/editor-theme-pack.jpg) | ![svg](docs/svg-export.jpg) |

## Run

```bash
./build.sh --engine      # publish the .NET engine into public/_framework (once, or after Core changes)
npm install
npm run dev              # http://localhost:5173

./build.sh               # full production build into dist/ (static files; any web server)
./build.sh --aot         # same, with the engine AOT-compiled (needs: dotnet workload install wasm-tools)
```

Pushes to `main` that touch the editor, its engine or Core publish `dist/` to GitHub Pages through
[`.github/workflows/pages.yml`](../../.github/workflows/pages.yml) (the site lives at `https://<owner>.github.io/<repo>/`). It needs
**Settings → Pages → Source = GitHub Actions** set once on the repository. The build uses relative URLs (`base: './'`), so it runs under
that sub-path unchanged. Run the workflow by hand from the Actions tab to redeploy without a push.

On Windows, use `build.ps1` with `-EngineOnly` / `-Aot`. Other scripts:

- `npm test`: vitest, engine logic, no browser.
- `npm run e2e`: browser checks against the real engine (headless Chromium + Playwright); see [`e2e/README.md`](e2e/README.md).
- `npm run typecheck`

Requirements: a WebGL2 browser with `EXT_color_buffer_float`. That covers every current desktop browser.

### Hosting, and why it can look stalled

- **Cold start** downloads about 5.5 MB of WebAssembly (the .NET runtime, the BCL and Core). Pages compresses on its own and caches for ten minutes; the files are fingerprinted, so a redeploy is picked up. The build drops the `.br`/`.gz` copies `dotnet publish` writes, since Pages never serves them.
- **Generation is the real cost.** The engine runs in the Mono interpreter: about 0.3 ms per tile on a recent laptop (detail 5, 10 thousand tiles: ~3 s; detail 6: ~11 s; detail 7: ~50 s and much more memory). A phone or an old machine is several times slower, which reads as a hang. The wizard times every generation on the device and shows the estimate per detail level; **Cancel** in the status bar stops a long one and starts a fresh engine.
- **A stalled or crashed engine is reported, not waited on:** the boot has a 90 s watchdog, a worker that dies (out of memory) fails its calls with the reason, and the next call starts a new worker.
- **AOT** compiles Core to native WebAssembly instead of interpreting its IL in the browser: generation is several times faster, the download is larger and the build slower. The Pages workflow builds AOT on every push (installing `wasm-tools`) and falls back to the interpreted build if the AOT one fails; the manual run has an `aot` input to turn it off. Locally it is `./build.sh --aot`. Not yet measured: the AOT download size and speed-up.

## What it does

| Area | Features |
|---|---|
| World | Seed, detail (icosphere level), plate count, cultures. Each plate is named by one race × culture from Core's name generator. |
| Features | Rivers, lakes, seas and oceans, mountain ranges, valleys and canyons, forests, a settlement hierarchy (capital, city, town, village) and 13 resource-deposit types. Features are extracted from Core's catalogs and named by Core's `NameGenerator`. "Sea" is a new place kind. |
| Inspect | Hover and click any feature to see its facts: area, length, discharge, climate, biome mix, deposits, culture. Rename it, re-roll its name in its own culture, zoom to it, or delete it. Delete also removes its label, river and marker, as one undo step. |
| Filters | View presets: Map, Plates, Plates + topography, Topography, Biomes, Resources. There are also per-layer toggles and per-kind and per-deposit filters (coal, iron, gems…). Hidden features cannot be picked. |
| Edit | Terrain brush (raise, lower, smooth, flatten, roughen, paint land, paint sea). Biome brush. Land-use brush (farmland, ashlands, pasture, built-up: drawn over the biome, never changing it). Climate brush (temperature and rainfall offsets, tinted while active). Stamps: place, scatter or erase. Select, move, rotate, scale, flip and delete stamps and labels. Text tool: new labels are named in the local culture. River tool. Every edit is undoable. |
| Text | All labels are vector glyph outlines (opentype.js). Baselines can be straight, arched, or follow a river. The same path data is drawn on screen and written to SVG. |
| Content | Packs carry themes, biomes, assets (procedural generator presets or authored SVG with palette tokens), placement rules and fonts. Packs layer by id. A rule with `"layout": "relief"` places ranges and hill country by height and ridge prominence: big peaks on the spine, smaller ones on the flanks, clumps with open ground between them, and scattered vegetation kept off their bodies. Zoomed out, stamps thin by on-screen size (forests and scrub first; ranges keep their biggest peaks). Try **Packs → Example**: `public/packs/example-pack.json`. |
| Theming | The live theme editor covers map colors, line weights, biome colors, stamp inks, typography per label kind, and the editor chrome. Edits go to a "My edits" overlay pack: non-destructive, resettable, exportable as a pack. |
| Files | `.wmap` (gzip; JSON header + raw typed-array rasters); SVG with Inkscape layers; PNG of the current view; `.wkt.csv` (columns `wkt,layer,kind,name,id,attrs`, EPSG:4326, rivers cut at the antimeridian; areas are their label anchor because the document stores them as tiles, not outlines). |

Keys:

| Keys | Action |
|---|---|
| I / V / H / B / G / S / T / W | Tools |
| `[` `]` | Brush size |
| Wheel | Zoom |
| Middle/right drag, or Space + drag | Pan |
| Ctrl+Z / Ctrl+Shift+Z | Undo / redo |
| Ctrl+S / Ctrl+O | Save / open |
| In Select: R, `[` `]`, F, Del | Rotate, scale, flip, delete |

## Architecture

```
React views (src/ui)          dumb components: render state, call commands
   │ useSyncExternalStore
View models (src/viewmodels)  MVVM: one per panel; immutable state snapshots + commands; DOM-free
   │
Editor (src/engine/Editor.ts) Facade / composition root: owns every service, exposes use cases
   ├─ WorldService ──postMessage──► world.worker: Core (.NET wasm) → rasterize → features → names
   ├─ MapDocument                 aggregate root: rasters + entity stores + world reference data
   ├─ UndoStack + Commands        every mutation (raster CoW blocks, entity add/remove/update, composite)
   ├─ Tools (Strategy) + ToolController (DOM input → tool calls, universal pan/zoom)
   ├─ PackRegistry → AssetLibrary (atlas + SVG symbols), FontLibrary → LabelCache
   └─ MapRenderer (WebGL2) + Overlay (Canvas 2D) + SvgExporter (+ export.worker)
```

- **Patterns:**
  - Observer: a typed `Emitter` on documents, stores, selection, undo and packs.
  - Command, with coalescing (a slider drag is one undo step) and Composite.
  - Strategy (tools), Proxy (`WorldService`), Facade (`Editor`), MVVM.
- **Separation:** tools see the editor only through `ToolContext`. View models see only the `Editor`. Nothing under `src/engine` imports React.
- **Platform services:** file pick and save are injected into view models as a `FileService`.
- **Themes are data.** The renderer reads colors and widths from the theme every frame. Stamp inks are resolved when the atlas is built (palette tokens), so switching themes re-inks the map without touching the document.

### Projection and styling

The map is **Mercator, clipped at ±79.17°**, giving a 2048×1536 map-unit (mu) square-pixel document:

- Mercator is conformal, so shapes and wave rings keep their proportions. Equirectangular stretched the spike's north and south coasts.
- **Every stylization is computed in map space:** domain warp, detail noise, SDF distances, ripple spacing, stamp and label sizes. Ripples therefore have the same width and spacing at every latitude. Nothing is styled on the sphere and then projected.

### Rendering pipeline (incremental)

| Pass | Resolution | Notes |
|---|---|---|
| field | 2× (4096×3072) | elevation, domain-warped by cylinder noise (wraps in x) plus detail noise |
| seed | 2× | sub-texel zero crossings as **relative** offsets (RG16F, half the memory of absolute RG32F) |
| JFA | 2× | **bounded** jump flood, steps 64→1, reach 127 px |
| SDF | 2× | signed distance in mu (R16F): coast = 0, ripple ring k = −k·ring |
| colorize | 1× | biome ids → theme palette with an 8-tap blend |
| display | screen | per frame; mode (styled, biomes, elevation, plates) and layer flags in one shader |

- **Brush strokes become physics on demand.** A stroke only repaints pixels; **Apply physics** (terrain and biome panels) makes it count:
  - The pixel difference against what the engine last produced is averaged per icosphere tile into a sparse edit table. Sculpt edits are added to the tectonic relief *before* climate and erosion, so a drawn ridge casts a rain shadow, sheds talus and drains like any mountain. Fix edits pin the final height instead. A repainted biome (covering 40% of a tile) pins that tile's biome: hydrology classifies its cover from it and the biome stage keeps it.
  - **Climate paint** is a pair of signed-byte rasters (`warmth`, `wetness`, full scale ±0.4 temperature / precipitation units) kept in the document and saved in the `.wmap` (v3+; older files read as unpainted). Unlike height and biome strokes it is not diffed against a baseline: it is an input, re-read whole on every apply, and each tile takes the mean paint under it (below 0.01 it counts as a stray dab). The engine adds the offset last, so winds, rain shadows and ocean currents are untouched elsewhere. Revert clears it; undo restores it.
  - **Land use** (farmland, ashlands) is its own byte raster (`landuse`, `.wmap` v4), drawn over the biome by the renderer. A biome is what the climate grows; farmland is what people did to it, so it sits on Plains, Forest or Savanna without destroying them, and the physics neither reads nor rewrites it (Apply and Revert leave it alone; Clear removes it). Its ids 1-4 are Core's `LandUseId`, and on Apply each land tile that is at least 40% one painted use becomes a `PinLandUse` pin in the edit table (the raster stays the authority: erase the paint and the pin goes; water is never pinned). Core never reads a pin back into the physics, so this changes nothing visible by itself; it is what a host's allocation plugins see as "the author decided this" (`docs/API.md`, *Land use*). Plugin-allocated land use is deliberately **not** composited into this raster: it would then look like hand paint and be re-pinned on the next Apply.
  - **Climate** (View > Climate, with Temp / Rain / Currents / Seasons and a legend; presets *Climate* and *Winds & currents*) colours the whole map by what Core's climate stage produced, and **Wind arrows** / **Current arrows** draw the prevailing wind and the sea currents (red = warms the coast, blue = cools it; spacing follows the zoom). While any of them is on, the status bar reads out the tile under the cursor: temperature and rain index, wind and current direction and strength, the seasonal swing in K, and any painted offset from the Climate tool. Like capacity it is derived: Apply refreshes it, undo restores it, `.wmap` stores it as an optional `climate` blob (nine floats per tile; older files show nothing until the next Apply). Temperature is the engine's 0..1 index, not a thermometer: one unit is about 55 K, and the latitude profile is linear, so mid-latitudes read colder than Earth's.
  - **Land capacity** (View > Land capacity, with a Farming/Grazing/Game/Timber/Fish selector and a hover readout in the status bar) tints each tile by what it could carry, from Core's `LandCapacityStage`. It is derived, so it lives with the physics state: Apply refreshes it, undo restores it, and `.wmap` stores it as an optional `capacity` blob (older files simply show nothing until the next Apply). Coarse worlds over-credit farmland (see `docs/API.md`). Files saved before v4 painted these as biome ids 11 and 13: on load those pixels move into the land-use layer and the biome takes the nearest ordinary biome, so old maps open looking the same.
  - The planet (axial tilt, stellar flux, spin) is set in the Generate panel, stored in the `.wmap` settings (an older file reads as Earth) and handed to the engine on every apply, so re-deriving a tilted world keeps it tilted.
  - The climate model has its own Generate-panel section: re-run climate after erosion, diagonal (Coriolis) wind, sea-current warmth and rain, and wind-erosion passes. They are stored in the `.wmap` settings (an older file reads as the calibrated defaults), handed to the engine on every apply, and switching refresh off also restores the river threshold that single-pass climate was calibrated at.
  - `WorldExports.ApplyEdits` (tiles, sculpt deltas, pinned heights, flag/biome words, temperature and precipitation offsets) replaces the engine's edit table and runs `World.Recompute()`: elevation, climate, erosion, hydrology, biomes and the river, lake, range and deposit catalogs, never the plate simulation. The call is pure in (seed, edits), so it is idempotent and never double-erodes. It is global (a ridge can send a river basin elsewhere), which is why it is a button and not part of the dab.
  - Identity survives. New features are matched to old ones by shared tiles or shared riverbed (`world/reconcile.ts`), not by ordinal id, so names carry over; deleted engine features are tombstoned and stay deleted; hand-made rivers, stamps and labels are untouched; land use (below) is never touched. Savanna and Mediterranean are Core biomes (warm ground with a summer wet season, and temperate ground with a winter one and a dry summer; the climate layer's "Wet season" shows which, brown for summer rain and teal for winter rain), so painted savanna or Mediterranean is pinned like any other biome; a map saved before savanna was a Core biome, with savanna painted, pins it on its next Apply. A cached climate array from before the rain phase existed is widened on load (the season reads as unknown until the next Apply physics).
  - One undo step swaps terrain, biomes, features, rivers, labels, stamps and the edit table together. **Revert** drops the table and reproduces the generated world exactly. A `.wmap` stores the sparse table (never the derived rasters); on opening, the baseline is rebuilt from it deterministically.
  - Sub-tile detail is the price: the table is per tile, so the applied raster is the engine's, not your pixels.
- **Edits are incremental.** A brush dab dirties a rect D:
  - The flood runs on D ⊕ 2·reach.
  - Only D ⊕ reach is committed.
  - A region that crosses the antimeridian becomes a wrapped full-width strip.
- **Edits within a frame are coalesced**, so N dabs cost one upload and one rebuild.
- **Undo is copy-on-write in 64×64 blocks.** Its cost scales with what a stroke touched, not with the map size.
- **Stamps** are drawn in one instanced draw call (sorted by y), from a mipmapped, premultiplied atlas.
- **Rivers** are inked ribbons: every reach is drawn in bank ink (`riverInk`, default the land ink), then every reach's water inset by the bank width (`riverInkWidth`), so confluences open into one channel and thin streams stay pen lines. Width follows discharge (about 0.8 mu at a source to 3.5 mu); sources taper to a hairline and mouths flare. A tributary runs straight into its parent's drawn line (Core stops it a tile short), and mouths are clipped to the drawn shore. SVG export draws the same two layers.
- **The last map comes back.** The working map is autosaved to the browser (IndexedDB) a few seconds after each change and when the tab is hidden, and reopened on the next visit instead of the wizard. It is a whole `.wmap` snapshot (a few MB gzipped), not a delta, since rasters and paint change anywhere. *New world…* / *Blank* replace it; private windows or blocked storage simply start fresh. *Save* still writes a real file.
- The **New world wizard** (opens at start when nothing is stored; turn that off in its last step, or use *New world…*) walks through planet presets (Earthlike, tropical, arid, ice age, violent seasons, slow and fast spinners, with tilt, starlight and spin diagrams), land (tectonic presets, detail with a time estimate from this device), peoples, realms, and the map's look (style, coastline, decals, name styles). Nothing is generated until *Create*.
- **Realms** (a wizard step) choose who founds the world: history length and realm count, the peoples realms are drawn from (human, dwarf, three elves, orc, halfling, gnome, lizardfolk, dragonfolk, catfolk, dogfolk; each culture has a form affinity such as wood elves leaning loose-association) and founders pinned to a people, a government form and a latitude/longitude (anything left blank is drawn). The choice is saved in the document, so Apply physics rebuilds the same history.
- **Settlements** are history's when the world has a history: every standing `HistorySettlement` is a place on its own tile (a polity seat is a capital, then city, town and village by tier; a hamlet draws as a village), carrying its realm, role, size, founding turn and population in the inspector. The site search only places settlements when history is off. A realm names its places in the name style its people's culture renders through (the culture's `NamingTable`; a human realm takes one of the human styles you picked), and any style a realm needs is added to the world's styles. Land no realm holds takes the nearest realm's style, else one style per landmass (`tileNamingStyles`, saved as `tileStyle`). What history left is on the map too: **ruins** (ended settlements and ruin scars), **holy sites** (`HolyFound` events) and **battlefields** (war scars), as `ruin`, `shrine` and `battlefield` features with their own filters, labels, stamps (`core.ruins`, `core.temple`) and a `sites` WKT layer; one mark per tile, living settlements first. Craters, plague pits and other scars are not drawn.
- **History view.** The document keeps what the run left (`doc.history`: realms, the event log, territory as a base map plus changes; saved in the `.wmap`), so it works after *Open*. The **History** tab has a turn scrubber, the realm list and an event log; the **Realms** and **Realm borders** layers tint and outline territory at the scrubbed turn, and while the scrubber is held back, places, settlement stamps and roads that did not exist yet are hidden (a town that later fell shows only as its ruin, from the turn it fell). Selecting a town, city, village, ruin, holy site or battlefield shows who held its tile and what happened there. *Open history window…* shows a timeline of every realm or people, their figures, titles and event log; titles are awarded by the fixed rules in `achievements.ts` (a TODO there: make them scriptable).
- **Roads** come from history, not the physics: the engine's `HistoryRoads()` (every `AddRoad` proposal, Core's trade-road operator or any plugin's) is chained into routes per (route ends, keeper), given a small wander off the tile lattice, rounded by corner cutting and cut where it would cross open water (short wet patches are bridged). A route with no keeper is a trunk between capitals (solid); one kept by a polity is a spur (dashed); one a war cut keeps its line, dotted. They are `doc.roads` (saved in the `.wmap` header, optional for older files), drawn by `RoadLayer` in `road` ink (default the land ink, `roadWidth`), toggled by the Roads layer, and written as stroked paths by the SVG export. Roads need no plugin API of their own: a plugin that emits `AddRoad` shows up here.
- **Lakes** are carved into the elevation about halfway from a ponded tile to its dry neighbours: the ponded share is blurred across face edges and thresholded with noise, so shores are rounded bays rather than every triangle touching a lake tile.
- **Names are unique per map** (case-insensitive): a generated, re-derived or re-rolled name that is already taken is re-rolled in its own culture with a derived seed (`world/uniqueNames.ts`).
- **Feature outlines** (hover, selection, picking) follow what is drawn: water and forest features take the water or forest pixels on and just past their tiles; ranges and valleys round their tile outline and keep off water (`features/featureMask.ts`, cached per feature).
- **River names** sit on the straightest stretch long enough for them, on a smoothed, offset copy of the river, spaced by arc length along that offset line.
- **Labels** are cached Path2D objects; each label's layout is cached by a signature.
- **SVG export:**
  - Reads back the SDF, contours it in a worker (marching squares, wraps in x, Douglas-Peucker, then Chaikin), and writes Inkscape layers.
  - Shared geometry (the coast, each label's outline) is defined once and referenced with `<use>`.
  - Stamps are `<symbol>`s.

## Measured

Headless Chromium in a CI container:

- **CPU timings** (Core, worker JS, main-thread JS) are representative.
- **GPU work runs on SwiftShader**, a CPU rasterizer. One idle frame takes about 0.4 s there, and a full-field JFA rebuild takes seconds. On any real GPU these passes take milliseconds. GPU numbers below are therefore marked as SwiftShader numbers, not editor costs.

| World generation, seed 7 | size 5 (10,242 tiles) interpreted | size 5 AOT | size 6 (40,962 tiles) interpreted | size 6 AOT |
|---|---|---|---|---|
| .NET runtime boot (worker) | 193 ms | 212 ms | | |
| Core `Build()` | 690 ms | **205 ms** | 3,555 ms | **1,034 ms** |
| Pull arrays across interop | 94 ms | 13 ms | 558 ms | 82 ms |
| Rasterize to 2048×1536 (JS, worker) | 148 ms | 143 ms | 260 ms | 209 ms |
| Feature extraction (JS, worker) | 27 ms | 27 ms | 102 ms | 90 ms |
| Name all named features (one batched Core call) | 112 ms | 23 ms | 91 ms | 34 ms |
| Populate stamps (main) | 102 ms (22,943 stamps) | 106 ms | 161 ms | 136 ms |
| Label placement (main) | 182 ms | 179 ms | 154 ms | 153 ms |

The rest is SwiftShader GPU work, not editor cost:

| Operation | Time |
|---|---|
| 40-dab terrain stroke, JS side (dabs + copy-on-write) | 9 ms |
| `.wmap` save | 0.67 s, 12.5 MB |
| `.wmap` load | 0.58 s; rasters and entities are byte-identical after the roundtrip |
| SVG export, full map | ~2.5 s, 17.5 MB (≈9 MB of it is IM Fell's rough glyph outlines) |

- **Download:** `_framework` is ~1.5 MB brotli interpreted, ~2.9 MB with AOT.
- **AOT:** about 3.4× faster `Build()`, for about 1 minute of extra build time.
- **App JS:** 200 kB, plus React 141 kB and opentype.js 172 kB (69 + 45 + 50 kB gzip).

## Findings

- **`self.onmessage = …` deadlocks the .NET runtime in a worker.**
  - `dotnet.create()` never resolves when the worker assigned `onmessage` before booting. The runtime uses the worker's message channel itself.
  - The fix is `self.addEventListener('message', …)`.
  - This costs nothing to avoid, but it is silent: no error, no rejection.
- **Projection, not shader tuning, fixed the polar distortion.** Once styling is computed in map space on a conformal projection, coastal waves look the same at 70° as at the equator.
- **Feature extraction belongs in JS, not Core.**
  - Core already exposes rivers, water bodies, ranges, deposits and city sites as catalogs.
  - The cartographic layer (ocean divided into basins at its straits, settlement hierarchy, label anchors, details text) is presentation. It runs in about 30 ms in V8.
  - Naming stays in Core, batched into one interop call.
- **Labels as outlines, not text:**
  - The SVG looks the same everywhere, with no font installation.
  - Hit-testing and collision use the exact geometry.
  - The cost is file size. Outline precision is 0.01 mu.

## Not done

- **Mobile:** there is no layout for small screens beyond a collapsed sidebar.
- **Regional maps:** there is no local azimuthal projection for detailed regional sheets. The document format leaves room for one.
- **Hand-drawn assets:** the core pack is procedural. Authored SVG assets are supported but not shipped.
