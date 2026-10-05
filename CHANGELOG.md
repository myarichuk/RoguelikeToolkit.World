# Changelog

## [1.0.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v0.3.0...v1.0.0) (2026-10-11)


### ⚠ BREAKING CHANGES

* ArenaDefaults is removed. The ArenaAllocator parameter is gone from TectonicPlateGenerationStage(int, int, ArenaAllocator?) and TectonicPlateLayer(WorldDataStore, int, int, ArenaAllocator?); drop the argument. The layer already ignored it. The stage's Dispose() is kept but now does nothing.

### Features

* drop SharpArena; tectonic scratch uses reused managed buffers ([e875cea](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e875cea5ee54579dd7359a1e14a1b158e1440925))

## [0.3.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v0.2.0...v0.3.0) (2026-10-11)


### Features

* Unity packages (world.core, world.scripting.jint) + release-please publishing ([92453b3](https://github.com/myarichuk/RoguelikeToolkit.World/commit/92453b3d704a96dbd35e624362597323ff58ac36))
* Unity packages (world.core, world.scripting.jint) and release-please publishing ([fbd5bf2](https://github.com/myarichuk/RoguelikeToolkit.World/commit/fbd5bf279b72286fa9849037450b85f244735e9f))
