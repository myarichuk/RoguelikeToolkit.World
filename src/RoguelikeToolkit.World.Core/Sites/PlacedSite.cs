namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>
/// One injector-placed site on a region/local map. CellIndex addresses the
/// materialized tier map (region cell or local tile). NameSeed is an opaque
/// draw from the injector's dedicated stream for game-side name generation.
/// Tags are free-form labels (e.g. "mine", "iron", "ancient").
/// </summary>
public sealed class PlacedSite
{
    /// <summary>What sort of site this is.</summary>
    public SiteKind Kind { get; init; }
    /// <summary>Cell of the materialized tier map the site occupies (region cell or local tile).</summary>
    public int CellIndex { get; init; }
    /// <summary>Alias for <see cref="CellIndex"/> (local-tile indexing).</summary>
    public int TileIndex => CellIndex;
    /// <summary>Extent of the site in cells around <see cref="CellIndex"/> (0 is a single cell). Informational: the library does not use it for queries.</summary>
    public int FootprintRadius { get; init; }
    /// <summary>Opaque seed for game-side name generation, drawn from the placing injector's own RNG stream.</summary>
    public uint NameSeed { get; init; }
    /// <summary>Free-form labels describing the site, such as "mine" or "ancient".</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    /// <summary>Added to the danger level of the site's cell; rounded to the nearest integer when applied.</summary>
    public float DangerDelta { get; init; }
    /// <summary>Added to the habitability score of the site's cell.</summary>
    public float HabitabilityDelta { get; init; }
    /// <summary>Id of the injector that produced the site; the pipeline fills it in when an injector leaves it empty.</summary>
    public string InjectorId { get; init; } = string.Empty;

    /// <summary>Returns a copy with <see cref="InjectorId"/> replaced; every other field is carried over, and <see cref="Tags"/> is shared, not cloned.</summary>
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
