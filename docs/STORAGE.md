# Storage design — planet-scale terrain, 100k+ NPCs, few allocations

The library keeps four kinds of data and gives each its own storage, because they differ in size, mutability and lifetime.

| Tier | What | Storage | Allocates |
|---|---|---|---|
| 1. Planet fields | elevation, climate, hydrology, biome per hex | one memory-mapped array per layer (`WorldDataStore`) | nothing after generation |
| 2. Static features | rivers, water bodies, ranges, deposits | managed catalogs + `SpatialIndex` (flat masks / sorted tile arrays) | at build only |
| 3. Derived detail | region and local maps | pure function of (seed, address); never stored | per derivation (see below) |
| 4. Mutable state | NPCs and anything the game adds | `Entities.EntityStore`: chunked columns, handle ids | nothing per spawn |

Rule of thumb: **generated terrain is derived, game state is stored, and the library never needs to know what an NPC is.** The library provides the substrate (columns, ids, spatial buckets, snapshots); the game defines the columns.

## Why the planet is the small part

Size 7 is 163,842 hexes at roughly 116 bytes each, about 19 MB. Size 8 would be about 76 MB. The cap on planet size comes from generation time and mesh size, not storage. Region and local maps are effectively unbounded, which is why they are derived, not stored.

## Tier 1 — planet fields

- `GetRef<T>` / `GetSpan<T>` resolve the layer through a dense per-type id (array index), not a `Type` dictionary lookup. Measured at size 4: 1000 `GetRef` calls dropped from 4.8 µs to 0.9 µs, the same as a hoisted span.
- Still best practice in hot loops: fetch the span once.
- `World.QueryRadius` compares dot products against `cos(radius / R)` and only calls `Acos` for hits (about 100x faster per query at size 4).
- Plate-wide data lives in a table, not on every hex: `TectonicPlate` is 16 bytes per hex (was 72). Drift and base elevation are stored once per plate in a `PlateInfo` row (`store.RegisterTable<PlateInfo>()`, read with `GetTable<PlateInfo>()`), and a hex reaches its row through the 1-based `Id`. At size 7 that is 2.6 MB instead of 11.8 MB. The table reserves at least 256 rows (10 KB) so a stage can use a different plate count than the layer; more than that fails with a clear error.

## Tier 2 — features

- `SpatialIndex` builds from boolean masks and emits ascending tile lists with one scan per kind: no hash sets.
- It shares the topology's tile-vector array instead of copying it (about 4 MB saved per index at size 7).
- `NearestFeature` / `NearestDeposit` no longer allocate: the cell ordering lives on the stack.

## Tier 4 — entities

`EntityId` is `(Index, Generation)`. The generation is odd while the slot is alive and even while free, so a stale handle (a despawned NPC) can never alias a recycled slot. `default` means "no entity".

`EntityStore` holds typed **columns** of unmanaged structs:

```csharp
var store  = new EntityStore();
var place  = store.AddColumn<Place>();     // game-defined structs
var person = store.AddColumn<Person>();    // register every column up front

var id = store.Create();                   // zeroed; ~11 ns, 0 bytes allocated
place[id]  = new Place { Tile = tile };
person[id] = new Person { NameSeed = seed };
store.Destroy(id);
```

### Frequent additions (on-demand NPCs)

- Columns are **chunked** (1024 slots per chunk). Growing adds a chunk per column; nothing is copied or moved. So a spawn never hitches, never lands on the large object heap, and never invalidates a span, even one held by a scan that is in progress.
- Iterate by chunk:

  ```csharp
  for (int k = 0; k < person.ChunkCount; k++)
  {
      var span = person.Chunk(k);                  // slot = (k << EntityStore.ChunkShift) + i
      for (int i = 0; i < span.Length; i++) { /* skip dead: store.IsAliveSlot(...) */ }
  }
  ```
- **Idempotent spawning** with `EntityKeyMap`: derive a stable key from (world seed, place, index), look it up, and only generate and create the NPC when it is not already present. A player who walks away and comes back meets the same NPC. If the NPC was despawned, the key resolves to a dead handle and is overwritten on the next spawn.

  ```csharp
  ulong key = EntityKeyMap.MakeKey(world.Seed, tileIndex, spawnIndex);
  if (!map.TryGetLive(store, key, out var id))
  {
      id = store.Create();
      map.Set(key, id);
      // fill columns deterministically from the key / seed (names via FantasyNameGenerator)
  }
  ```
- Keep the key in a `Column<ulong>` and call `map.Rebuild(store, keys)` after loading a snapshot.
- `Reserve(n)` pre-allocates chunks if you want to move the one-chunk-per-1024-spawns cost out of gameplay.

### Spatial lookup

`CellIndex` is a counting-sort bucket index: cell (usually a planet hex) to the contiguous span of entity slots in it. It rebuilds from a chunked column with a struct selector, and it allocates nothing once warm:

```csharp
struct TileOf : ICellOf<Place> { public int Cell(in Place p) => p.Tile; }
index.Rebuild(world.TileCount, store, place, new TileOf());
foreach (int slot in index.InCell(tile)) { ... }          // plus world.Map.DataStore.GetAdjacent for neighbours
```

### Persistence

`Save(Stream)` / `Load(Stream)` write raw little-endian columns (generations, free list, then each column preceded by its type hash and stride). Loading validates the column set first and leaves the store untouched on mismatch. Suggested pattern: periodic snapshots plus a game-owned journal of changes. The library does not provide the journal yet.

### Measured (300k entities on a 163,842-hex planet, Release)

| Operation | Time | Allocated |
|---|---|---|
| Create 300k entities | ~2.5 ms | chunk arrays only (~7 MB, once) |
| Spawn one on demand (steady state) | ~11 ns | 0 |
| Rebuild the whole tile index | ~1.7 ms | 0 |
| Scan every NPC (age +1) | ~0.4 ms | 0 |
| Entities in a hex and its 6 neighbours | ~7 ns | 0 |

Numbers are from short BenchmarkDotNet runs on one machine; re-run `tests/RoguelikeToolkit.World.Benchmarks` (`EntityBenchmarks`, `QueryBenchmarks`) for your own.

## Tier 3 — derived maps (not done yet)

`GetRegion` and `GetLocal` allocate a new grid on each call (about 7 KB and 22 KB at default sizes). At exploration speed that is fine, but a byte-budgeted LRU cache with pooled arrays and disposable handles would remove the garbage for a game that revisits the same areas. Not built; measure first.

## Not done yet

- Change journal and incremental snapshots for entities.
- Aggregate (non-individual) simulation for NPCs far from the player: the substrate supports it (an "aggregate" is just another column set), but the policy belongs to the game.
- A pooled/cached region tier (above).
