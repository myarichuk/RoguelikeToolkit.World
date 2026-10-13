# Changelog

## [2.0.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v1.2.0...v2.0.0) (2026-10-13)


### ⚠ BREAKING CHANGES

* **core:** ErosionGenerationStage.Iterations = 0 no longer disables the whole stage (use Enabled = false); GeoWkt output changes for water bodies, ranges and empty geometry.
* **core:** 2.0 namespaces are per slice (RoguelikeToolkit.World.Core.<Slice>), LocalMap{Info,Layer,GenerationStage} are Biome*, ten helper types are internal, persisted formats are incompatible (store 3->4, entity snapshot 3->4, delta 1->2) and out-of-tree plugins must be recompiled. See docs/MIGRATION_2.0.md.
* **core:** addresses and bounds to Topology, SpatialIndex to new Queries slice
* **core:** internalise stage-contract, solver and data-file plumbing
* **core:** StageOrder constants replace bare stage order ints
* **core:** persisted type ids no longer depend on namespaces
* **core:** rename LocalMap{Info,Layer,GenerationStage} to Biome*
* **core:** all public types except the facade moved namespace. Persisted WorldDataStore / EntityStore files written by 1.x fail their header check because the header hashes Type.FullName (tracked for a follow-up).

### Features

* **core:** document the full public API and enforce it in the build ([598ae95](https://github.com/myarichuk/RoguelikeToolkit.World/commit/598ae954b97e216eb6e442bd555f5e402816066a))


### Bug Fixes

* **core:** persisted type ids no longer depend on namespaces ([cfe4e64](https://github.com/myarichuk/RoguelikeToolkit.World/commit/cfe4e64be7245a707a976847109eb36846e17ae9))
* **core:** resolve Phase 6 findings (QueryRadius options, catalog ids, GetRef tables, erosion Enabled, WKT) ([68bb8ac](https://github.com/myarichuk/RoguelikeToolkit.World/commit/68bb8ac4989b2e08cbbd760ffdc1c4dd4a5a9789))


### Code Refactoring

* **core:** addresses and bounds to Topology, SpatialIndex to new Queries slice ([1bb51e9](https://github.com/myarichuk/RoguelikeToolkit.World/commit/1bb51e9d38d42d645ea02b5dc20e815820cb2dfb))
* **core:** internalise stage-contract, solver and data-file plumbing ([2472af9](https://github.com/myarichuk/RoguelikeToolkit.World/commit/2472af98eae56938d53108e79c0d9050d68d4bee))
* **core:** namespaces follow folders ([70101bb](https://github.com/myarichuk/RoguelikeToolkit.World/commit/70101bba4980050abd320caf99a4bf9672f87855))
* **core:** rename LocalMap{Info,Layer,GenerationStage} to Biome* ([c24076a](https://github.com/myarichuk/RoguelikeToolkit.World/commit/c24076a323feecc2855d0b173391ac3ba9c5d189))
* **core:** StageOrder constants replace bare stage order ints ([2acec13](https://github.com/myarichuk/RoguelikeToolkit.World/commit/2acec13283dc88047eb8277565bb036c4df6eee5))

## [1.2.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v1.1.0...v1.2.0) (2026-10-13)


### Features

* **core:** Mediterranean biome from winter-wet rain ([cf8cd90](https://github.com/myarichuk/RoguelikeToolkit.World/commit/cf8cd907f236bda651d7529fe1cdee868ce0545f))
* **core:** model sea ice as a derived ocean mask, and show it in the map editor ([0a83394](https://github.com/myarichuk/RoguelikeToolkit.World/commit/0a83394a7c0a6ac6afb5d197a55da0b510e9b5dc))
* **core:** sea ice feeds the city-site scorer ([9ebb884](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9ebb8840930416c928b28a316a19c2853ef6f840))


### Bug Fixes

* **core:** calibrate the sea-ice pack line against Earth and pin both ice lines tightly ([7fad05a](https://github.com/myarichuk/RoguelikeToolkit.World/commit/7fad05a4b476dcc3e28457247840ae8109bac6be))
* **core:** local maps inherit the season of the rain, so savanna and Mediterranean tiles stay themselves ([e81a385](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e81a385e7a5b89d75fc4a57cc2d20f0725eb1a53))
* **editor:** land-use tooltips say what it does, including the pin Apply physics sends ([6e633b2](https://github.com/myarichuk/RoguelikeToolkit.World/commit/6e633b2f6b50efd34c9866f42d7a80ddebd9524b))
* review issues and remediate ([95f0685](https://github.com/myarichuk/RoguelikeToolkit.World/commit/95f068588d03f161ba6e3d664dbc077ea49b3bc2))

## [1.1.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v1.0.0...v1.1.0) (2026-10-12)


### Features

* browser map editor + dual license (PolyForm Noncommercial / commercial) ([1557063](https://github.com/myarichuk/RoguelikeToolkit.World/commit/1557063d821c4ac8ff7475337e6c698fff6ce3a4))

## [1.0.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v0.3.0...v1.0.0) (2026-10-11)


### ⚠ BREAKING CHANGES

* ArenaDefaults is removed. The ArenaAllocator parameter is gone from TectonicPlateGenerationStage(int, int, ArenaAllocator?) and TectonicPlateLayer(WorldDataStore, int, int, ArenaAllocator?); drop the argument. The layer already ignored it. The stage's Dispose() is kept but now does nothing.

### Features

* drop SharpArena; tectonic scratch uses reused managed buffers ([e875cea](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e875cea5ee54579dd7359a1e14a1b158e1440925))

## [0.3.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v0.2.0...v0.3.0) (2026-10-11)


### Features

* Unity packages (world.core, world.scripting.jint) + release-please publishing ([92453b3](https://github.com/myarichuk/RoguelikeToolkit.World/commit/92453b3d704a96dbd35e624362597323ff58ac36))
* Unity packages (world.core, world.scripting.jint) and release-please publishing ([fbd5bf2](https://github.com/myarichuk/RoguelikeToolkit.World/commit/fbd5bf279b72286fa9849037450b85f244735e9f))
