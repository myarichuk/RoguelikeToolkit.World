# Changelog

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
