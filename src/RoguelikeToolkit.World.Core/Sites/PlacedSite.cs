namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>
/// One injector-placed site on a region/local map. CellIndex addresses the
/// materialized tier map (region cell or local tile). NameSeed is an opaque
/// draw from the injector's dedicated stream for game-side name generation.
/// Tags are free-form labels (e.g. "mine", "iron", "ancient").
/// </summary>
public sealed class PlacedSite
{
    public SiteKind Kind { get; init; }
    public int CellIndex { get; init; }
    /// <summary>Alias for <see cref="CellIndex"/> (local-tile indexing).</summary>
    public int TileIndex => CellIndex;
    public int FootprintRadius { get; init; }
    public uint NameSeed { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public float DangerDelta { get; init; }
    public float HabitabilityDelta { get; init; }
    public string InjectorId { get; init; } = string.Empty;

    public PlacedSite WithInjector(string injectorId) => new()
    {
        Kind = Kind,
        CellIndex = CellIndex,
        FootprintRadius = FootprintRadius,
        NameSeed = NameSeed,
        Tags = Tags,
        DangerDelta = DangerDelta,
        HabitabilityDelta = HabitabilityDelta,
        InjectorId = injectorId,
    };
}
