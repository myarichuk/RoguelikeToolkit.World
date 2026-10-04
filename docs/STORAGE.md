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

`Save(Stream)` / `Load(Stream)` write raw little-endian columns (generations, free list, then each column preceded by its type hash and stride). Loading validates the column set first and leaves the store untouched on mismatch. `Save` starts a delta chain. `SaveDelta(Stream)` then writes only what changed since the previous save or delta, and `LoadDelta(Stream)` applies the next one:

```csharp
store.Save(full);            // occasionally: a checkpoint
store.SaveDelta(d1);         // frequently: only touched chunks
store.SaveDelta(d2);

restored.Load(full);         // restore: full snapshot, then each delta in order
restored.LoadDelta(d1);
restored.LoadDelta(d2);
```

Writes go through spans and refs, so changes are found by hashing each chunk (64-bit) rather than by hooks: you never mark anything dirty. A delta holds the whole free list plus every generation and column chunk whose bytes differ (new chunks always count), so a single edit costs about one 1024-slot chunk per column it touches. A delta applies only to the chain it came from and only in order; a stray, skipped or stale delta throws `InvalidDataException` and leaves the store unchanged. A new full `Save` starts a new chain, so older deltas stop applying. Compact by saving a fresh full snapshot and discarding the old chain. Snapshot format is v3 (adds the chain id and step), so older v2 snapshots are rejected.

### Measured (300k entities on a 163,842-hex planet, Release)

| Operation | Time | Allocated |
|---|---|---|
| Create 300k entities | ~2.5 ms | chunk arrays only (~7 MB, once) |
| Spawn one on demand (steady state) | ~11 ns | 0 |
| Rebuild the whole tile index | ~1.7 ms | 0 |
| Delta save (one chunk touched) | ~1.5 ms | ~20 KB |
| Scan every NPC (age +1) | ~0.4 ms | 0 |
| Entities in a hex and its 6 neighbours | ~7 ns | 0 |

Numbers are from short BenchmarkDotNet runs on one machine; re-run `tests/RoguelikeToolkit.World.Benchmarks` (`EntityBenchmarks`, `QueryBenchmarks`) for your own.

## Tier 3 — derived maps (measured, no cache)

`GetRegion` and `GetLocal` allocate a new grid on each call. Measured (Release, size-4 world, short job):

| Call | Time | Allocated |
|---|---|---|
| `GetRegion` | ~6 us | 6.6 KB |
| `GetLocal` | ~23 us | 21.9 KB |

Even redrawing a 3x3 block of local maps every frame costs about 0.2 ms and about 200 KB of gen0 garbage, which is cheaper than a cache lookup plus eviction bookkeeping. **Decision: no LRU or pooling.** Revisit only if a game derives hundreds of maps per frame; re-run `QueryBenchmarks` (`DeriveRegionMap`, `DeriveLocalMap`) to check.

## Aggregate population

NPCs far from the player are not entities. `PopulationGrid` keeps a head count per (cell, group), where a group is whatever the game buckets by (species, faction, profession), stored cell-major so a tick is one linear sweep. `Step(policy, adjacency, seed)` applies the game's `IPopulationPolicy` (birth, death and migration rates, plus how attractive a cell is as a destination) and advances `Tick`.

- **Deterministic and order-free.** Each cell draws from its own sub-stream of (seed, tick, cell) and the step writes into a second buffer, so a cell never sees a half-updated tick. Save/Load keeps the tick, so a resumed run matches an uninterrupted one.
- **Migration conserves people exactly**; births and deaths are the expected value stochastically rounded (right mean, no demographic noise of their own). Migrants split over neighbours by attraction, and with nowhere attractive to go they stay.
- **Budgeting.** A full planet tick (163k tiles x 8 groups) takes about 11 ms with zero allocation. Pass a `cells` subset to step only part of the world (everything outside the loaded area, or a round-robin slice per frame).
- **Crossing the boundary.** When the player arrives, `Take` people out of the cell and spawn entities under deterministic `EntityKeyMap` keys; when they leave, `Give` the survivors back. That keeps head counts conserved, and which components an individual gets is the game's call. Keep the load radius smaller than the unload radius so a player at the edge does not cause churn.
- **Not delta-saved.** The grid is dense (a few MB per planet), so it has only a full `Save`/`Load`.

## Not done yet

- Individual identity across the boundary: a person who is taken out and given back returns to the head count, not to a named record. A game that needs persistent named NPCs should keep those as entities permanently and exclude them from the grid.
