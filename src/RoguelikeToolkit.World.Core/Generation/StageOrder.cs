namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>
/// The <see cref="IDeclaredStage.Order"/> of every built-in stage. Stages run in ascending order and the
/// <see cref="StageContractValidator"/> rejects a pipeline in which a stage reads a layer before its first writer.
/// Plugins pick a value relative to these (for example <c>Climate + 3</c>) instead of a bare literal, so a
/// future renumbering cannot silently reorder them.
/// </summary>
public static class StageOrder
{
    /// <summary>Plate partitioning and crust assignment; writes <c>TectonicPlate</c>.</summary>
    public const int Tectonics = 10;

    /// <summary>Base elevation from plates; writes <c>ElevationInfo</c>.</summary>
    public const int Elevation = 15;

    /// <summary>Climate model; writes <c>ClimateInfo</c>.</summary>
    public const int Climate = 16;

    /// <summary>Hydraulic erosion and island-chain stamping; refines <c>ElevationInfo</c>. Also the re-run point for <c>World.Recompute</c>.</summary>
    public const int Erosion = 17;

    /// <summary>Climate refresh after erosion and before hydrology.</summary>
    public const int ClimateRefresh = 17;

    /// <summary>Flow routing, lakes, rivers; writes <c>HydrologyInfo</c>.</summary>
    public const int Hydrology = 18;

    /// <summary>Biome classification; writes <c>BiomeInfo</c>.</summary>
    public const int Biomes = 20;

    /// <summary>Land capacity (settlement scoring input).</summary>
    public const int LandCapacity = 25;

    /// <summary>Final land-use resolution; always last.</summary>
    public const int LandUse = 99;
}
