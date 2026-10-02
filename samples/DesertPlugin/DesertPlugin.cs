using System;
using RoguelikeToolkit.World.Core;

// Published plugins reference the shared contracts from NuGet instead of a
// project reference:
//
//   <ItemGroup>
//     <PackageReference Include="RoguelikeToolkit.World.Core" Version="0.2.0" />
//   </ItemGroup>
//
// and need nothing else: IWorldGeneratorStage, WorldGeneratorStageAttribute,
// the layer structs (ElevationInfo, ClimateInfo, ...), WorldMap/WorldDataStore,
// and Rng all come from that package. Build the plugin, drop the resulting
// DesertPlugin.dll next to your other plugins, and point the pipeline at the
// directory:
//
//   pipeline.Discover("Plugins", d => Console.Error.WriteLine(d.Message));
//
// The host loads each DLL into its own collectible AssemblyLoadContext (shared
// Core identity, plugin-local dependencies) and unloads it on ResetStages.
// Plugins are full-trust: only load DLLs you trust.

/// <summary>
/// Dries the world out: scales precipitation down everywhere (order 19 runs
/// after hydrology 18, before biomes 20, so rivers and biomes both see the
/// drier climate). Declares Reads+Writes on ClimateInfo to join the legal
/// read-modify-write refinement chain — a blind second writer would throw at
/// ValidateContracts/Execute time instead of silently winning.
/// </summary>
[WorldGeneratorStage(19, Reads = new[] { typeof(ClimateInfo) }, Writes = new[] { typeof(ClimateInfo) })]
public sealed class AridityStage : IWorldGeneratorStage, ISeededStage
{
    public int Seed { get; set; } = 42;

    /// <summary>Fraction of precipitation kept. 0.5 = twice as dry.</summary>
    public double Dryness { get; set; } = 0.5;

    // Parameterless ctor is required: discovery instantiates via
    // Activator.CreateInstance. Configure knobs after discovery by querying
    // pipeline.Stages.OfType<AridityStage>().
    public AridityStage()
    {
    }

    public void Execute(WorldMap map)
    {
        var climate = map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < climate.Length; i++)
        {
            // Deterministic per-tile jitter keeps the drying from looking flat.
            double jitter = Rng.Create(Seed, i).NextDouble() * 0.1;
            climate[i].Precipitation = (float)(climate[i].Precipitation * Dryness + jitter * 0.05);
        }
    }
}
