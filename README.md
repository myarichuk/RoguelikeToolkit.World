# RoguelikeToolkit.World

A .NET 10 solution containing a core class library (`RoguelikeToolkit.World.Core`) for generating and querying spherical hex maps using icosahedral subdivision, and a cross-platform Avalonia 11 UI visualizer (`RoguelikeToolkit.World.App`).

## Core Library Architecture

The `RoguelikeToolkit.World.Core` library relies on two primary pillars to achieve zero-allocation, high-performance execution:

### 1. Centralized Data Storage: `WorldDataStore`
The hex sphere data structure is backed by a single memory mapped file, represented by the `WorldDataStore` class. This store serves as the single source of truth for all queryable tile data in the world grid.
- All layer data (such as `TectonicPlate` and `LocalMapInfo`) are unmanaged blittable structs.
- Layers are registered with the `WorldDataStore` prior to allocation (`DataStore.RegisterLayer<T>()`). The store dynamically calculates byte offsets for all registered layers to store them contiguously.
- When `Allocate()` is called, a single `MemoryMappedFile` is initialized, guaranteeing zero-allocation, cache-friendly lookups thereafter.
- `WorldDataStore` caches per-size tile unit vectors (`GetTileVectors()`), so repeated generation runs avoid rebuilding spherical tile positions and repeated trig conversions in hot paths.

### 2. Pluggable Generation Pipeline: `WorldGenerationPipeline`
World generation logic is abstracted into discrete, pluggable pipeline stages (`IWorldGeneratorStage`). The visualizer (or any consuming application) simply sets up the pipeline, discovers the stages, and executes them over the `WorldMap`.

## Developing a New Pipeline Stage

To write and inject a new world generation step, follow these instructions:

### Step 1: Implement `IWorldGeneratorStage`
Create a new class that implements the interface. Implement your generation logic within the `Execute(WorldMap map)` method. Use `map.DataStore.GetSpan<T>()` to gain mutable, zero-allocation access to your underlying layer structs.

### Step 2: Apply `WorldGeneratorStageAttribute`
Decorate your class with the `[WorldGeneratorStage(Order = N)]` attribute to specify its execution order. Lower numbers execute earlier in the pipeline. Note that assigning duplicate order values to multiple stages will throw an `InvalidOperationException` upon discovery.

```csharp
using RoguelikeToolkit.World.Core;

[WorldGeneratorStage(30)]
public class MyBiomeGenerationStage : IWorldGeneratorStage
{
    public void Execute(WorldMap map)
    {
        var localSpan = map.DataStore.GetSpan<LocalMapInfo>();
        var tectonicSpan = map.DataStore.GetSpan<TectonicPlate>();

        for (int i = 0; i < localSpan.Length; i++)
        {
            // Apply logic to mutate localSpan based on tectonicSpan data
        }
    }
}
```

### Step 3: Injecting the Plugin
The `WorldGenerationPipeline` can discover stages via reflection. By default, calling `pipeline.Discover("Plugins")` merges discovered stages with manually added stages and skips stage types that are already present. If you want a clean rebuild from discovery only, call `pipeline.DiscoverAndReplace("Plugins")` (or `pipeline.ResetStages()` first).

Discovery failures are reported structurally: pass a diagnostics callback to `Discover(..., onDiagnostic)` to collect non-fatal load/activation errors, or omit the callback to fail fast with an `AggregateException`.
