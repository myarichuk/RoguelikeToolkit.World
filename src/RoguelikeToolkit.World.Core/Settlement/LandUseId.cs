namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// Ids of the exclusive land uses: what people (or fire) have done to a tile's ground, as opposed to what
/// the climate grows on it. 0 is "none". The ids are persisted (map documents store them), so they never
/// change meaning. 5..127 are reserved for Core; <see cref="FirstPluginId"/>..255 belong to plugins and
/// games, whose names and colours travel in their own content pack.
/// </summary>
public static class LandUseId
{
    /// <summary>Nothing decided for the tile.</summary>
    public const byte None = 0;
    /// <summary>Tilled fields. Draws on <see cref="LandCapacityInfo.Arable"/>.</summary>
    public const byte Farmland = 1;
    /// <summary>A burn scar or volcanic waste. A consequence, not a use: nothing is yielded.</summary>
    public const byte Ashlands = 2;
    /// <summary>Sown or fenced grazing land. Draws on <see cref="LandCapacityInfo.Pasture"/>.</summary>
    public const byte Pasture = 3;
    /// <summary>Built-up ground: a town or a city's footprint.</summary>
    public const byte Built = 4;
    /// <summary>First id available to plugins and games; 128 to 255 are theirs.</summary>
    public const byte FirstPluginId = 128;
}
