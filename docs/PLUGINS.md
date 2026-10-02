# Plugins: compiled C# stages and Jint script stages

Two plugin kinds, two trust tiers. Pick per stage, mix freely in one pipeline —
ordering and layer contracts are shared.

| | Compiled C# (`*.dll`) | Jint JavaScript |
|---|---|---|
| Trust | **Full trust** — same power as any referenced DLL. Only load DLLs you trust. | **Untrusted** — hardened sandbox: no CLR, no modules, no `eval`, no IO; statement/memory/time budgets. |
| Package | Reference `RoguelikeToolkit.World.Core` (NuGet, future publish; project reference while developing in this repo). | Reference `RoguelikeToolkit.World.Scripting.Jint` (brings Jint). |
| Speed | Native; may touch store spans directly. | Proxy helpers per tile; fine for tuning passes, slower for heavy loops. |
| Contract | `[WorldGeneratorStage(Order, Reads, Writes)]` attribute. | `JintStageSpec` with `Order` + `Reads`/`Writes`/`ReadsOptional` types. |
| Example | `samples/DesertPlugin/DesertPlugin.cs` | `samples/JintScripts/aridity.js` |

Default generation order (leave gaps for plugins): tectonics (10) →
elevation (15) → climate (16) → erosion (17) → hydrology (18) → biomes (20).

## Rules both kinds follow

- **Order decides execution.** Lower runs first. Duplicate `Order` values throw
  `InvalidOperationException` at `Discover`/`Execute` time. Conventional gaps:
  19 sits between hydrology and biomes (climate/biome tweaks), 21+ runs after
  biomes (danger/biome rebalancing).
- **Reads/Writes are the collision contract** (`StageContractValidator`).
  The first writer of a layer is fine; every later writer must also read it
  (read-modify-write refinement, e.g. elevation → erosion). A blind second
  writer throws instead of silently winning. Required `Reads` must have an
  earlier writer; `ReadsOptional` layers may be absent or written later (the
  stage degrades gracefully).
- **Layers must be registered before `Allocate()`.** A stage declaring a layer
  the map never registered fails fast with a `RegisterLayer<T>()` hint instead
  of a bare `ArgumentException` from `GetSpan`.
- **Determinism.** Same `(seed, stage set)` → same planet. Derive per-tile
  randomness from `(seed, tile)` (`Rng.Create(seed, tile)` in C#,
  `rand01(tile, salt)` in JS), never from call order or wall-clock time.
- **Discovery merges by type.** `pipeline.Discover(dir)` keeps already-added
  stages and skips re-discovered types; `DiscoverAndReplace(dir)` (or
  `ResetStages()` first) rebuilds from discovery only. Load/activation failures
  go to the `onDiagnostic` callback, or throw `AggregateException` without one.

## Compiled C# plugins (full trust)

1. Create a class library targeting `net10.0`:
   ```xml
   <ItemGroup>
     <PackageReference Include="RoguelikeToolkit.World.Core" Version="0.2.0" />
   </ItemGroup>
   ```
2. Implement `IWorldGeneratorStage`. You get the whole store API
   (`map.DataStore.GetSpan<T>()`, `GetRef<T>()`, `GetAdjacent()`,
   `GetGeoCoord()`, `IsLayerRegistered<T>()`).
3. Declare order + contracts with the attribute. Implement `ISeededStage`
   when the stage needs the world seed (set it from the host after discovery).
   A parameterless constructor is required (discovery uses
   `Activator.CreateInstance`); expose knobs as settable properties.
   ```csharp
   [WorldGeneratorStage(19,
       Reads = new[] { typeof(ClimateInfo) },
       Writes = new[] { typeof(ClimateInfo) })]
   public sealed class AridityStage : IWorldGeneratorStage, ISeededStage
   {
       public int Seed { get; set; } = 42;
       public double Dryness { get; set; } = 0.5;
       public AridityStage() { }
       public void Execute(WorldMap map)
       {
           var climate = map.DataStore.GetSpan<ClimateInfo>();
           for (int i = 0; i < climate.Length; i++)
               climate[i].Precipitation = (float)(climate[i].Precipitation * Dryness);
       }
   }
   ```
   Full example: `samples/DesertPlugin/DesertPlugin.cs`.
4. Build, drop the DLL into a plugins directory (dependencies beside it), load:
   ```csharp
   var pipeline = new WorldGenerationPipeline();
   pipeline.Discover(); // built-ins
   pipeline.Discover("Plugins", d => Console.Error.WriteLine(d.Message));
   foreach (var s in pipeline.Stages.OfType<AridityStage>()) s.Dryness = 0.35;
   pipeline.ValidateContracts(map); // optional early check (Execute checks too)
   ```
5. Hosting notes: each DLL loads into its own collectible
   `PluginLoadContext` — Core contracts stay shared (one
   `IWorldGeneratorStage` identity), plugin dependencies resolve from the
   plugin's own directory first. `ResetStages()`/`Dispose()` disposes stages
   and requests unload (cooperative: completes once nothing references plugin
   types and no thread runs plugin code). Discovery failures (bad DLL, missing
   ctor, version mismatch at activation) are diagnostics, not crashes.

## Jint script stages (untrusted)

1. Reference the scripting package and write a script that defines a global
   `execute()` function. The host calls it once per `Execute`.
2. Scripts see only these globals (everything else — CLR, `require`, `fetch`,
   `eval`/`Function`, `os`/`io` — is unavailable):

   | Global | Meaning |
   |---|---|
   | `TILE_COUNT`, `SEED`, `PARAMS` | tile count, stage seed, your params object |
   | `BIOME.Ocean/Plains/Desert/Forest/Mountain/Tundra/Jungle/Swamp/Glacier/Canyon` | biome ids (ints) |
   | `lat(i)`, `lon(i)` | tile center degrees |
   | `continentality(i)`, `orogeny(i)` | tectonics drivers (need `TectonicPlate` in Reads/ReadsOptional) |
   | `height(i)` / `setHeight(i, v)` | `ElevationInfo.Height` |
   | `temp(i)` / `setTemp(i, v)`, `precip(i)` / `setPrecip(i, v)` | `ClimateInfo` fields |
   | `flow(i)`, `surface(i)` | `HydrologyInfo` (read-only in v1) |
   | `biome(i)` / `setBiome(i, id)`, `danger(i)` / `setDanger(i, n)` | `LocalMapInfo` fields |
   | `rand01(tile, salt)` | deterministic `[0,1)` from `(seed, tile, salt)` |

   Reads require the layer in `Reads` (or degrade to `0` when listed in
   `ReadsOptional` and unregistered); setters require the layer in `Writes`
   and throw otherwise. Out-of-range tile/biome/danger values throw with the
   stage name attached.
3. Wire it up:
   ```csharp
   pipeline.AddStage(new JintWorldStage(new JintStageSpec
   {
       Name = "aridity-js",
       Order = 19,
       Source = File.ReadAllText("aridity.js"),
       Reads = new[] { typeof(ClimateInfo) },
       Writes = new[] { typeof(ClimateInfo) },
       Params = new Dictionary<string, object?> { ["dryness"] = 0.5 },
       Limits = new JintStageLimits
       {
           Timeout = TimeSpan.FromSeconds(5),
           MaxStatements = 250_000,
           MemoryLimitBytes = 16_000_000,
       },
   }));
   ```
   Samples: `samples/JintScripts/aridity.js` (climate drying, order 19),
   `samples/JintScripts/highland-danger.js` (danger rebalance, order 21).
4. Sandbox notes: fresh Jint `Engine` per `Execute` with `Strict`,
   string-compilation disabled, recursion capped, and statement/memory/timeout
   budgets; host delegates are the entire API surface. Budget breaches and JS
   errors surface as `InvalidOperationException` naming the stage. Scripts that
   need full store access or native speed should be compiled C# stages instead.

## Troubleshooting

- `Duplicate Order values found ...` — two stages share an `Order` (C# vs C#,
  or script vs C#). Renumber yours into a free gap.
- `... blind-writes 'X' already written by [...]` — declare `Reads` on your
  refining stage (or write a different layer).
- `... reads/writes layer 'X' but it is not registered` — the map didn't
  `RegisterLayer<X>()` (custom minimal pipelines) — register it before
  `Allocate()`, or list the layer under `ReadsOptional` and degrade gracefully.
- `Jint stage '...' must define a global function execute()` — the script ran
  but defined nothing callable (typo, or only helpers).
- `Jint stage '...' failed: ...` — JS error or budget breach; the message is
  the first JS error line, the inner exception carries the Jint details.
- Plugin DLL silently absent — `Discover` skips types without the attribute
  and merges (not duplicates) re-discovered types; pass the diagnostics
  callback to see load/activation failures.
