# Changelog

## [3.2.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v3.1.0...v3.2.0) (2026-10-26)


### Features

* **chronicle:** X7 lexicon, grammar and late renderer for the history log ([e06e303](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e06e3033b4080b0be826aec74b1b9eb07fbee08e))
* **editor:** chronicle prose in the editor, with the editor's own names ([269bae5](https://github.com/myarichuk/RoguelikeToolkit.World/commit/269bae5ddb7d66df7af3702ef1530ea503e0a895))
* **editor:** history view, realm borders, place history and per-realm naming ([2f05c9b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/2f05c9b75cbaeb60dcd4bcc30bcc78ab6b5f4b22))
* **editor:** history view, realm borders, place history, per-realm naming ([815c89f](https://github.com/myarichuk/RoguelikeToolkit.World/commit/815c89f5af9f606831cfb3ba71c32c1c9df6d9a7))
* **editor:** realm names avoid each other and place labels; ruins, holy sites and myths surface in the history view ([5cd1bd1](https://github.com/myarichuk/RoguelikeToolkit.World/commit/5cd1bd10d4dc1f1c1bfbcf520bbe17628f49fd75))
* **editor:** X8 expansion exports - persons, bonds, offices, battles, route states ([0dba099](https://github.com/myarichuk/RoguelikeToolkit.World/commit/0dba099603728768b818d655948a58dfd30951b4))
* **editor:** X8 history expansion in the map editor - persons, battles, routes ([bd6c27c](https://github.com/myarichuk/RoguelikeToolkit.World/commit/bd6c27c19f092beaa82bcd298219820af3185c0f))
* **history,editor:** data-driven territory costs with per-culture terrain affinity; names unique across realms and places ([b2fc92e](https://github.com/myarichuk/RoguelikeToolkit.World/commit/b2fc92e27bbc4a15786f028e2c87efa27fab642e))
* **history:** borders follow terrain; every people can rebel; founders drawn without replacement ([38a71f8](https://github.com/myarichuk/RoguelikeToolkit.World/commit/38a71f83e7cd3e977e409acb8e10939be9eb031f))
* **history:** X1 expansion kernel - persons, offices, relations, battles, route state ([96c7ccf](https://github.com/myarichuk/RoguelikeToolkit.World/commit/96c7ccf38f06727eb2dc27eef66ca4b2cbcc76bf))
* **history:** X2 the court - rulers, heirs, offices and deaths as data-driven persons ([62e7350](https://github.com/myarichuk/RoguelikeToolkit.World/commit/62e735051ec4e5f8aec229fb7e2087555af84930))
* **history:** X3 battles - tactics, ways of war and a data-driven battle resolver ([87d22ae](https://github.com/myarichuk/RoguelikeToolkit.World/commit/87d22ae1276e733c9af364d6e60e1cfc8142b6d4))
* **history:** X4 sieges, relief battles, forts, and per-culture settlement/fort/ruin stamps ([ba56616](https://github.com/myarichuk/RoguelikeToolkit.World/commit/ba5661689fe808d743d22fa2c0cd13565ab60978))
* **history:** X5 logistics network, winter quarters, waystations and road projection ([ccdf850](https://github.com/myarichuk/RoguelikeToolkit.World/commit/ccdf850bf69aef125b6bd07287cc97d97151a649))
* **history:** X6 commerce, merchant princes, outlaw bands and sponsored intrigue ([e02af2e](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e02af2eb240348984346c21f68e73f92b0eac07d))
* **history:** X9 the expansion is the default history ([9b39afb](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9b39afb2e5863f43f2d2a9c2fb3bb5d515adc60b))
* **names,editor:** realm titles and name kinds are per-people name data ([254a080](https://github.com/myarichuk/RoguelikeToolkit.World/commit/254a080086f3a8c3b5194bf676dca9fd85fa3ded))
* **names:** own-word preference is table data (OwnWordWeight, default 2) ([ef74200](https://github.com/myarichuk/RoguelikeToolkit.World/commit/ef74200bc78eaf4fec0278c835a2d1783fc06154))


### Bug Fixes

* **determinism:** DetMath everywhere, total-ordered maritime queue, seeded Jint Math.random, ordinal string ops ([daa1f6b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/daa1f6ba43ce208b90e3cf25e37049516f85ca7f))
* **editor:** name persons by their stable key, not their id ([8740ebb](https://github.com/myarichuk/RoguelikeToolkit.World/commit/8740ebb5da0f2cec5ee417bff8682821e8f8daa5))
* **hierarchy,picking:** unbiased local slope fit; flat-map and terrain picks hit the drawn surface ([783887b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/783887b3630d669bf41a290f6c96b7ea2f6d8c7c))
* **history:** regenerate starts clean, null-edit recompute no longer forks, old saves load ([a2b0384](https://github.com/myarichuk/RoguelikeToolkit.World/commit/a2b0384a7edf27f33cf5840c29d1b809f5737794))
* **history:** rehome displaced people, capital successor seats, dependent proposals, route-scoped road conflicts ([d7492ac](https://github.com/myarichuk/RoguelikeToolkit.World/commit/d7492ac03fbe0dd1ab670e138a503e143050afc5))
* **hydrology:** closed-lake basins drain into their lake; basin runoff feeds the lake ([90acdb8](https://github.com/myarichuk/RoguelikeToolkit.World/commit/90acdb8c11ecfa2ee7b67a9bf1d6d4105d02c44e))
* **hydrology:** landlocked marine basins are endorheic, open lakes drain through their outlet, rivers can dry out ([22f2064](https://github.com/myarichuk/RoguelikeToolkit.World/commit/22f2064bcd527cb19a34cc8b26ec59682f4820e9))
* **robustness:** release native stores on failed builds, enforce declared Jint reads, editor recovers from failed generate/boot ([9a46ae8](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9a46ae8bb0bde1c23715a359c2cb5d9fde877286))


### Performance Improvements

* **history:** X0 legacy fixtures, border index, faster deposit and city-site scoring ([76c010a](https://github.com/myarichuk/RoguelikeToolkit.World/commit/76c010ae0586926f061cbce811981acd68acf586))

## [3.1.0](https://github.com/myarichuk/RoguelikeToolkit.World/compare/v3.0.0...v3.1.0) (2026-10-19)


### Features

* **core:** history-first P1 - drift deltas and generalized mixed naming ([f5f8831](https://github.com/myarichuk/RoguelikeToolkit.World/commit/f5f8831e81c0da4b64d2e26435e79fcad8c85d57))
* **economy:** M3.17 tune fiscal operator; stop tax-cut spiral, soften scarcity unrest ([67c7e60](https://github.com/myarichuk/RoguelikeToolkit.World/commit/67c7e6001f0464b2030d127dc70d80acd92a64c3))
* **editor:** M3.19 plugin-agnostic history summary export for a future panel ([baf3e85](https://github.com/myarichuk/RoguelikeToolkit.World/commit/baf3e85d71166a5691cbe9d436b8db56d1e4bc8f))
* history generation, world creation wizard and some other stuff ([b3a491e](https://github.com/myarichuk/RoguelikeToolkit.World/commit/b3a491e53bfc2cf40787e041a4e75e59015fb15d))
* **history:** M1.1a kernel types - interned kinds, event links, importance compaction, polity lifecycle ([39f527c](https://github.com/myarichuk/RoguelikeToolkit.World/commit/39f527c54d5b9307af6c2a18971255f258fdd04c))
* **history:** M1.1b declared scalars and event indexes ([f4bc707](https://github.com/myarichuk/RoguelikeToolkit.World/commit/f4bc707dc0eb4c675b5eb76c39c42cecfc1688fd))
* **history:** M1.2 operator contract, turn view, proposal sink and driver ([a2e66d9](https://github.com/myarichuk/RoguelikeToolkit.World/commit/a2e66d924f3a8c718915a98be0769f37c205431a))
* **history:** M1.3 Conquest operator, SetOverlay proposal ([b03076b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/b03076b7d7a44ed36d8e651f6edb8ca7baaa637f))
* **history:** M1.3 Exchange operator, DriftOpenness proposal ([40a5054](https://github.com/myarichuk/RoguelikeToolkit.World/commit/40a505437cb536a6296f486915eba4a583962166))
* **history:** M1.3 Faith and CityHistoryProject post-passes; M1.3 complete ([e53e9df](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e53e9dfb14e21ff4694465a3ffd0c5f2868444e0))
* **history:** M1.3 HolySite operator ([ea565a2](https://github.com/myarichuk/RoguelikeToolkit.World/commit/ea565a2ace8cc95995c7c7d9046c25f0d902ee3c))
* **history:** M1.3 Khan operator ([9198cf3](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9198cf38adf46d5ed49584110b8609f4ffac4713))
* **history:** M1.3 Population operator, map access for operators, SetPopulation proposal ([d9abbb0](https://github.com/myarichuk/RoguelikeToolkit.World/commit/d9abbb07b76de5e0277ea0ff524fd4dbb7881c2a))
* **history:** M1.3 Rebellion operator, AddPolity pre-pass with re-flood and holy-site sync ([356444c](https://github.com/myarichuk/RoguelikeToolkit.World/commit/356444c463b925d4ccf9cd6faaed76368913b1a3))
* **history:** M1.3 TradeRoad operator ([af017fe](https://github.com/myarichuk/RoguelikeToolkit.World/commit/af017fe2aec64abf1051a2ff8455be2af6841017))
* **history:** M1.3 Treaty operator, SetHeir proposal ([3b743a6](https://github.com/myarichuk/RoguelikeToolkit.World/commit/3b743a63084fbddb11a7a4c521c6c95d7f862c8e))
* **history:** M1.3 Villain operator, AddVillain proposal ([e72ee2e](https://github.com/myarichuk/RoguelikeToolkit.World/commit/e72ee2edd9ad101e0278f78b1a44d617e1457ef9))
* **history:** M1.3 War operator, AddSite proposal, grudge turn stamps ([9a5b1eb](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9a5b1ebae417ced306a93c177739be6848ce8074))
* **history:** M1.4 schema v4 persistence (string table, scalars, lifecycle, event links, grudge stamps) ([560f84b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/560f84b75637b085682673a6ffd4de19ce6661d5))
* **history:** M1.5 wire StandardHistory driver into WorldBuilder, drop HistoryEventsStage, re-baseline corpus ([773b049](https://github.com/myarichuk/RoguelikeToolkit.World/commit/773b049735b61ba61fe944490aa5cb62b7b1a986))
* **history:** M2.1 settlement lifecycle fields, schema v5 ([99b8870](https://github.com/myarichuk/RoguelikeToolkit.World/commit/99b8870b2f0d14d48df4295c48808f91ff4e91d5))
* **history:** M2.10 history-weighted settlement placement (scars, ruins, routes) over scorer candidates; TurnView.Scars; corpus re-baseline ([ef36e95](https://github.com/myarichuk/RoguelikeToolkit.World/commit/ef36e95ff5f80658d6752e27ff80678f03a2e3a8))
* **history:** M2.3A capital settlements founded with their polity; M2.3 design ([08bf93c](https://github.com/myarichuk/RoguelikeToolkit.World/commit/08bf93c380b522ef9d1685885c56dacb645e3724))
* **history:** M2.3B AddSettlement/EndSettlement proposals, settlement events, TurnView settlements ([a981af0](https://github.com/myarichuk/RoguelikeToolkit.World/commit/a981af009cee1e4b51495b27057105f65dbd707c))
* **history:** M2.3C SettleOperator colonization, settlement owner sync, corpus re-baseline ([ed1292d](https://github.com/myarichuk/RoguelikeToolkit.World/commit/ed1292d7065a466eb0d307d7bf221e6bf63cfacc))
* **history:** M2.4A stored per-settlement population, catchment capacity, GrowSettlement, schema v6 ([b2cab2b](https://github.com/myarichuk/RoguelikeToolkit.World/commit/b2cab2b9ed96c822771dc45f6c322520a9a329f6))
* **history:** M2.4B displaced pool ledger, Release/Absorb proposals, EndSettlement auto-release, schema v7 ([06e875a](https://github.com/myarichuk/RoguelikeToolkit.World/commit/06e875adeaf83db6453c151606f4f9d8b88f0b7e))
* **history:** M2.4C refugee settling: pool groups found Refuge settlements or merge into a standing one ([9a10ac0](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9a10ac037bc45589e933380f33b6bf42411749a6))
* **history:** M2.5A shock resolver: Shock, ShockResolver, KillPopulation, Shock event, EndSettlement.LostShare ([9a1a35e](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9a1a35ef1d8a4a9673bdd30c6affb5a728c28260))
* **history:** M2.5B per-settlement capacity and pressure on TurnView; refugees prefer roomy settlements, shocks hit crowded ones harder ([3534bc6](https://github.com/myarichuk/RoguelikeToolkit.World/commit/3534bc6c9b4619b0b9aafe14294e4b95aed9eb5a))
* **history:** M2.6A hazard maps and Hazard operator: quake, volcano, tsunami, flood, drought shocks; corpus re-baseline ([b6e05e6](https://github.com/myarichuk/RoguelikeToolkit.World/commit/b6e05e64d8d34eaeb00f7e1a15361b84675fda14))
* **history:** M2.6B famine and plague shocks from settlement pressure and tier/road density; corpus re-baseline ([cbc5647](https://github.com/myarichuk/RoguelikeToolkit.World/commit/cbc56473f71647c5c3559aa254fcd2632678ba27))
* **history:** M2.7 scars: HistoryScar, AddScar proposal, ruins and shock scars projected as sites and tile modifiers; schema v8 ([2b364a3](https://github.com/myarichuk/RoguelikeToolkit.World/commit/2b364a3beb193d6ca5d123aacc26104e917712c3))
* **history:** M2.8 spur roads between a polity's settlements; plague links count live routes at the settlement; corpus re-baseline ([d1821fc](https://github.com/myarichuk/RoguelikeToolkit.World/commit/d1821fc0aec6dd697138fd3774208dff4f545dcc))
* **history:** M2.9A wars sack, raze and capture settlements: CaptureSettlement proposal, SettlementCaptured event, owner sync limited to reassigned tiles; corpus re-baseline ([9d65fcb](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9d65fcb300a5d59cf48336c6ef09e920d41804a9))
* **history:** M2.9B cross-polity asylum for homeless polities' displaced groups ([fdb1346](https://github.com/myarichuk/RoguelikeToolkit.World/commit/fdb134666bae209f0aa91f95c588e83c734a1118))
* **history:** M3.1 fold war battlefield sites into battlefield scars; scar conflicts per tile and kind; corpus re-baseline ([44a2211](https://github.com/myarichuk/RoguelikeToolkit.World/commit/44a221153a5e97380bd7d7ce7e3ce0ec012efcb1))
* **history:** M3.10 public API audit; regenerate PublicAPI backlog, remove dead AddSite proposal, public PeoplesLoader, guard internals exposure ([78eab8d](https://github.com/myarichuk/RoguelikeToolkit.World/commit/78eab8d5bb15565bb224a3127b69f8afdd080165))
* **history:** M3.11 plugin state bag; keyed versioned blobs in the catalog (schema 9), survive Respatialize, reset notes ([bc2fa3a](https://github.com/myarichuk/RoguelikeToolkit.World/commit/bc2fa3a9457f4300352074e9986f340d9b5449e7))
* **history:** M3.12 reserved plugin order band (100-899) and namespace/name plugin names; AddPluginOperator, DeclarePlugin ([07079c5](https://github.com/myarichuk/RoguelikeToolkit.World/commit/07079c5c290e5367d0098fde27d70db364da70e9))
* **history:** M3.13 reference plugin host and sample; WithHistoryScalars, HistoryPluginHost, samples/HistoryPlugin ([042e5a9](https://github.com/myarichuk/RoguelikeToolkit.World/commit/042e5a915e17d421ac80f3de67fddbf5ec901005))
* **history:** M3.14-15 economy plugin outside Core; plugin event kinds and operator-written state bag (schema 10) ([75b72da](https://github.com/myarichuk/RoguelikeToolkit.World/commit/75b72daed44396743b444c4d3b2c2ee4e2ab7683))
* **history:** M3.2 river and fishing scoring terms; holy-site pull in settlement placement; corpus re-baseline ([1e99a61](https://github.com/myarichuk/RoguelikeToolkit.World/commit/1e99a61b5545f4d804ff70e1ddcd5a02948f830e))
* **history:** M3.3 tier operator with hysteresis; ChangeTier proposal and SettlementTierChanged event; corpus re-baseline ([db0eb9a](https://github.com/myarichuk/RoguelikeToolkit.World/commit/db0eb9ad323cb85897ea8bebfed6b9a292870352))
* **history:** M3.4 garrison, pilgrimage, route-node and horde-sedentarization founding sources; why tag on SettlementFounded; corpus re-baseline ([28834fe](https://github.com/myarichuk/RoguelikeToolkit.World/commit/28834fe7c46032c3a12b33d94adb8bee250f5b6d))
* **history:** M3.6 polity collapse into successors; SplitPolity proposal, PolityCollapsed event, MaxSuccessors; pins on ended polities inert; corpus re-baseline ([79edf5e](https://github.com/myarichuk/RoguelikeToolkit.World/commit/79edf5eecd7fd9ce8a7bb9923ce4f541b2058cf7))
* **history:** M3.7 coastal collapse successors become merchant republics; republic route nodes; river-mouth-on-route placement weight; corpus re-baseline ([0e0d743](https://github.com/myarichuk/RoguelikeToolkit.World/commit/0e0d743c649a5a83bbc045fd1110c9f75511add5))
* **history:** M3.9 polity absorption (merge); AbsorbPolity proposal, PolityAbsorbed event, absorption myth; corpus re-baseline ([5a897e8](https://github.com/myarichuk/RoguelikeToolkit.World/commit/5a897e8e9abadc7ae0c6f7b3dc06700f269dd8ff))
* **history:** P2c operators (holy sites, khan, treaty, villain, faith) and M1.0 coverage tests ([531d0a7](https://github.com/myarichuk/RoguelikeToolkit.World/commit/531d0a70a6de1c5e6bf8339c1cfc64ed76daeb9a))
* implementing history WIP ([518730d](https://github.com/myarichuk/RoguelikeToolkit.World/commit/518730d4a37053312060b9438e71e14c620fe79a))


### Bug Fixes

* bug fix and improvements pass ([3521f88](https://github.com/myarichuk/RoguelikeToolkit.World/commit/3521f88bc2a248545dcf0e7681758c791319973c))
* **history:** M3.18 give rebellions their own allowance above the founding cap ([9dc2cdb](https://github.com/myarichuk/RoguelikeToolkit.World/commit/9dc2cdb1e7fe4fe3b7831e3095d9e3d8b3388daf))
* **history:** M3.5 ignore pins on capital tiles; report unknown folks in user compat rules ([52b7129](https://github.com/myarichuk/RoguelikeToolkit.World/commit/52b71297e44fe595b4e82858b2cc0e49e022d1a4))
* **history:** trade gate compares grain shares so routes can open ([32ce8f2](https://github.com/myarichuk/RoguelikeToolkit.World/commit/32ce8f2d09fd7b8cab63c965cd339d9fbeb5dd8a))

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
