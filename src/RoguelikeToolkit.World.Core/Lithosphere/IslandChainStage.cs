using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>
/// Breaks the continuous land ribbons that oceanic plate boundaries paint (see
/// <see cref="IslandChainDetector"/>) into strings of discrete islands. Runs on the *eroded* relief:
/// erosion moves mass back out of any strait cut earlier (thermal creep refills them within a few
/// iterations), so a repair made before it does not survive. Hand sculpting and pinned tiles are left alone.
/// <para>
/// Its order ties with erosion (17); the pipeline's sort is stable, so adding it after erosion runs it
/// after erosion, before the climate refresh and hydrology, which then read the repaired coast.
/// <see cref="WorldBuilder.WithIslandChains"/> does that.
/// </para>
/// </summary>
[Preserve]
public sealed class IslandChainStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed, ISeededStage
{
    public int Seed { get; set; } = 42;

    /// <summary>Detection and repair tuning.</summary>
    public IslandChainOptions Options { get; set; } = new();

    public IslandChainStage()
    {
    }

    public IslandChainStage(int seed)
    {
        Seed = seed;
    }

    public string Name => "IslandChains";
    public int Order => ErosionGenerationStage.PipelineOrder;
    public Type[] Reads => new[] { typeof(ElevationInfo), typeof(TectonicPlate) };
    public Type[] ReadsOptional => new[] { typeof(UserEditInfo) };
    public Type[] Writes => new[] { typeof(ElevationInfo) };

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var edits = store.IsLayerRegistered<UserEditInfo>() ? store.GetSpan<UserEditInfo>() : default;
        IslandChainDetector.Break(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>(), Seed, Options, edits);
    }
}
