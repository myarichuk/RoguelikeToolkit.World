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

    /// <summary>Culture seeding: the peoples roster this world's history draws on. Fills the attached history catalog; writes no tile layer.</summary>
    public const int CultureSeed = 26;

    /// <summary>Polity formation: capitals, forms and tile ownership from capacity, water and deposits. Fills the attached history catalog; writes no tile layer.</summary>
    public const int PolityForm = 27;

    /// <summary>First order of the history-operator band (war, conquest and the other conflict operators plug in at 28..90). The turn driver lives here.</summary>
    public const int HistoryEvents = 28;

    /// <summary>Trade-road builder: opens surplus-complementary routes between capitals.</summary>
    public const int TradeRoad = 30;

    /// <summary>People and time: logistic growth, pressure, succession and trait-drift ticks.</summary>
    public const int Population = 32;

    /// <summary>Settle operator: colonization into owned frontier land, after the population step.</summary>
    public const int Settle = 34;

    /// <summary>Tier operator: settlements grow and shrink along the tier ladder with the people they hold.</summary>
    public const int Tier = 35;

    /// <summary>Hazard operator: physics-driven shocks (quake, volcano, tsunami, flood, drought) drawn against the hazard maps.</summary>
    public const int Hazard = 36;

    /// <summary>Holy-site raising: high-surplus owned tiles become shrines with sheltering auras.</summary>
    public const int HolySite = 44;

    /// <summary>Steppe ultimatum: horde khans demand tribute from weaker neighbors (submit or resist).</summary>
    public const int Khan = 46;

    /// <summary>Treaty operator: abstract adult-only marriage/foster alliances, refusals log grudges.</summary>
    public const int Treaty = 48;

    /// <summary>War operator: raids between neighboring polities; severs roads, raises pressure.</summary>
    public const int War = 50;

    /// <summary>Conquest operator: resolves wars into culture overlays when the attacker is stronger.</summary>
    public const int Conquest = 60;

    /// <summary>Rebellion operator: far-flung, oppressed regions break away (bounded by the polity cap).</summary>
    public const int Rebellion = 70;

    /// <summary>Villain operator: long-life actors rise to rule, freezing successions (bounded by the villain cap).</summary>
    public const int Villain = 76;

    /// <summary>Exchange operator: road-volume diffusion (loanwords, openness drift) along active routes.</summary>
    public const int Exchange = 80;

    /// <summary>Faith operator: per-polity forms, pressure-drawn powers and myth slots from the event log.</summary>
    public const int Faith = 84;

    /// <summary>City-history projection: polity capitals become settlements and historic sites. Last history order before the land-use allocation band closes.</summary>
    public const int CityHistoryProject = 91;

    /// <summary>Final land-use resolution; always last.</summary>
    public const int LandUse = 99;
}
