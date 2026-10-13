# Changelog

## [3.0.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v2.0.1...v3.0.0) (2026-10-13)


### ⚠ BREAKING CHANGES

* **core:** default world generation changed. Call WithLegacyPlates() to keep existing seeds producing the same worlds.

### Features

* **core:** make balanced plates the default world generator ([28c399b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/28c399b9ba430e491f7bdbb08ace62c9c3005422))
* **core:** make diffusion and glacial erosion independent of tile order ([fb7044b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/fb7044bca761809c0e95ce9dcbe2e0b32186b14c))
* **core:** opt-in balanced plates (even seeding, bounded plate size, coast off the plate borders) ([5441433](https://github.com/myarichuk/RoguelikeToolkit.World/commit/5441433d0f6d064d57cb14ccefea62da462f1fdb))
* **core:** shear-aware boundaries with a soft gate, and oceanic-oceanic polarity ([6567619](https://github.com/myarichuk/RoguelikeToolkit.World/commit/6567619fa48e9d81b3504536c39c2aa61088975c))
* **core:** split the default world into separate continents ([be1dc1d](https://github.com/myarichuk/RoguelikeToolkit.World/commit/be1dc1d4961c1aeea7d05a8b62ae93a21ce467d8))
* **core:** stop small worlds from coming out mostly land ([fbb0c55](https://github.com/myarichuk/RoguelikeToolkit.World/commit/fbb0c55012875fd3a49108128e2659c64fb43de5))


### Performance Improvements

* **core:** sort each drainage routing once, with packed keys ([7a9e21f](https://github.com/myarichuk/RoguelikeToolkit.World/commit/7a9e21f6fd30c55473399376adcfc40b3022d912))

## [2.0.1](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v2.0.0...v2.0.1) (2026-10-13)


### Bug Fixes

* **core:** resolve symlinks in the file-store path check, validate stage knobs, document threading ([2fa7de2](https://github.com/myarichuk/RoguelikeToolkit.World/commit/2fa7de2166cc153db179dc5cbd82a31386bd6679))
* **core:** validate entity snapshot/delta free lists and bound header-driven allocation ([a43fc5f](https://github.com/myarichuk/RoguelikeToolkit.World/commit/a43fc5f70506e557201b657ea2ad171388c6d2fb))
* **editor,jint:** make regeneration, edits and script writes fail safely ([3c2f480](https://github.com/myarichuk/RoguelikeToolkit.World/commit/3c2f480d2622b2076142723902413f0e00e1e378))

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
