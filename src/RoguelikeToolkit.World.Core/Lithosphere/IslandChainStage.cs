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
    /// <summary>Seed for the random bead and gap lengths along each chain; the result is deterministic for a given world and seed. Defaults to 42.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Detection and repair tuning.</summary>
    public IslandChainOptions Options { get; set; } = new();

    /// <summary>Creates the stage with seed 42 and default <see cref="Options"/>.</summary>
    public IslandChainStage()
    {
    }

    /// <summary>Creates the stage with the given <paramref name="seed"/> and default <see cref="Options"/>.</summary>
    public IslandChainStage(int seed)
    {
        Seed = seed;
    }

    /// <summary>Diagnostic name, <c>IslandChains</c>.</summary>
    public string Name => "IslandChains";
    /// <summary><see cref="Generation.StageOrder.Erosion"/>: ties with erosion and runs after it when added later.</summary>
    public int Order => StageOrder.Erosion;
    /// <summary>Elevation and plate layers, which must be registered.</summary>
    public Type[] Reads => new[] { typeof(ElevationInfo), typeof(TectonicPlate) };
    /// <summary>Hand edits: sculpted or height-pinned tiles are never drowned.</summary>
    public Type[] ReadsOptional => new[] { typeof(UserEditInfo) };
    /// <summary>Elevation, lowered on the strait tiles.</summary>
    public Type[] Writes => new[] { typeof(ElevationInfo) };

    /// <summary>Detects island chains and drowns the strait tiles between beads by lowering them to the gap floor below sea level (see <see cref="IslandChainDetector.Break"/>).</summary>
    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var edits = store.IsLayerRegistered<UserEditInfo>() ? store.GetSpan<UserEditInfo>() : default;
        IslandChainDetector.Break(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>(), Seed, Options, edits);
    }
}
