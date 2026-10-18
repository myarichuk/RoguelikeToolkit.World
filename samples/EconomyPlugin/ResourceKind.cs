namespace EconomyPlugin;

/// <summary>The goods the economy tracks, derived from the map's land capacity layer.</summary>
public enum ResourceKind
{
    /// <summary>Arable plus pasture.</summary>
    Food = 0,
    /// <summary>Standing timber.</summary>
    Timber = 1,
    /// <summary>Fish on sea, lake and river tiles.</summary>
    Fish = 2,
    /// <summary>Huntable game.</summary>
    Game = 3,
}
