# Assessment: how useful is this for a procedural RPG or roguelike, and what is missing

Written after reviewing the name generator in depth and skimming the rest of the library (README, API/ARCHITECTURE/PLUGINS docs, the site/history/materialization code, the stage pipeline). The name system was read line by line; the geography stack was read at API level and exercised through its tests, not audited.

## What it is good at

**A trustworthy geography substrate.** A seeded, deterministic planet (tectonics, elevation, erosion, climate, hydrology, biomes, deposits) with lazy region and local zoom that stays consistent with its parent, backed by a flat memory-mapped store. For a roguelike or sandbox this is the hard, unglamorous part, and the determinism contract plus storageless derivation ("you pay per zoom, not per planet") is the right shape: you can persist a seed and a small override list instead of a world.

**Clean extension points.** Ordered stages with declared layer contracts, compiled-plugin and sandboxed Jint script stages, site injectors, and a query-time `IHistoricalContext` and `IMaterializationStore` so games own mutable state. Not storing history and player changes inside the generator is the correct split.

**Placement primitives a game can use immediately.** City-site scoring (fresh water, fertility, danger, history deltas), ruins/mines/landmarks, deposits, spatial index, fast lookups.

**Names that carry meaning.** After this pass: gendered, culturally structured people names (Hebrew, Russian, Arabic, Japanese, Chinese, Irish, Welsh, English, plus 20 fantasy folk), place names that follow the surrounding terrain and the language's own word order, etymology on every part, theme-biased generation, three scripts, data-driven and extensible with YAML only. For a DM-style tool or a procedural game this is a genuine differentiator, because glossed names let later systems say *why* a place is called what it is.

## Gaps, roughly by value

1. **No history generation (the biggest one).** `HistoryHooks` is only an interface plus a site index: the library consumes history but never produces any. Caves-of-Qud-style depth (founding myths, wars, dynasties, migrations, ruins that are ruins *for a reason*, artifacts with provenance) is what makes a generated world feel lived-in, and today every game must build it from scratch. This is the natural next module.
2. **No political/cultural layer.** Nothing assigns cultures, factions, borders, trade routes or roads to the map, so a name style is chosen by the caller rather than inherited from where the place is. Cultures-as-regions (spreading from founding sites across the terrain) is the missing link between geography and names, and it is also the substrate history needs.
3. **No entity generation beyond names.** NPCs (profession, family ties, age, traits), organizations (guilds, temples, mercenary companies), creatures/ecology by biome, items and loot, settlement layout and buildings. `PlacedSite` stops at a position, a kind, tags and a `NameSeed`.
4. **No tactical or settlement-scale maps.** By design the library stops at roughly 0.25-4 km hexes; the docs say tactical maps are game-owned. That is a defensible boundary, but a town-layout or dungeon-footprint helper would cover the most common need.
5. **Simulation over time.** No weather/season model, no resource flow or economy, no population growth. These are the inputs history simulation wants.
6. **Names: remaining holes.** No organization/tavern/item naming; no titles or honorifics; no family-tree consistency (siblings sharing a surname, children inheriting); no world-wide uniqueness registry; Russian/Arabic place names cannot yet inflect adjectives for gender or case (worked around with compound forms); only seven languages' worth of authenticity; fantasy folk tables are still small (about 20 stems and 3 clans each) and their places borrow fantasy_common vocabulary.
7. **Tooling.** There is a viewer and a name tester, but no scenario-level preview (map + names + sites together), and no headless UI tests for the Avalonia apps.

## Procedural history as data-driven plugins: how I would shape it

The existing seams already suggest the design:

- **A `HistoryStage`-style pipeline**, parallel to the geography stages: ordered, declared inputs/outputs, deterministic from the world seed, shippable as compiled plugins or Jint scripts exactly like `IWorldGeneratorStage`. Output is a game-neutral event log plus a *projection* implementing `IHistoricalContext` (so everything downstream, including city scoring and danger, works unchanged).
- **Data-driven eras, events and actors in YAML**, the same philosophy as the name tables: event templates with preconditions ("a settlement with a deposit and a hostile neighbour"), effects (found city, raze city, migrate culture, create ruin, spawn artifact) and gloss/tag output. A "ruleset" plugin then becomes a bundle of YAML plus optional script hooks, so a game can ship `dark-age.yaml` or `desert-empires.yaml` without touching C#.
- **Culture and faction layer first**, since events need actors: seed cultures on good city sites, expand them over the hydrology/biome costs the library already has, and bind each to a name style. This single layer fixes gap 2 and gives history something to act on.
- **Names and history feed each other**: event templates ask the name generator for a place/person name in the owning culture's style, and the stored glosses let the log say "Kfar Zahav, named for the gold that ruined it".
- **Keep the ownership rule**: the library generates the log deterministically from the seed, the game stores any divergence (player actions) in the materialization store, and queries blend both.

Suggested order: culture/faction regions, then a minimal event engine (found, raze, migrate, ruin) with YAML templates, then artifacts/legends, then ruleset plugins and a viewer overlay.
