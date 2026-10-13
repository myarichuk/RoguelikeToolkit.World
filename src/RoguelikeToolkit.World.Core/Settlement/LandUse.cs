using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Ids of the exclusive land uses: what people (or fire) have done to a tile's ground, as opposed to what
/// the climate grows on it. 0 is "none". The ids are persisted (map documents store them), so they never
/// change meaning. 5..127 are reserved for Core; <see cref="FirstPluginId"/>..255 belong to plugins and
/// games, whose names and colours travel in their own content pack.
/// </summary>
public static class LandUseId
{
    public const byte None = 0;
    /// <summary>Tilled fields. Draws on <see cref="LandCapacityInfo.Arable"/>.</summary>
    public const byte Farmland = 1;
    /// <summary>A burn scar or volcanic waste. A consequence, not a use: nothing is yielded.</summary>
    public const byte Ashlands = 2;
    /// <summary>Sown or fenced grazing land. Draws on <see cref="LandCapacityInfo.Pasture"/>.</summary>
    public const byte Pasture = 3;
    /// <summary>Built-up ground: a town or a city's footprint.</summary>
    public const byte Built = 4;
    public const byte FirstPluginId = 128;
}

/// <summary>
/// Uses that overlap with a <see cref="LandUseInfo.Use"/> and with each other: a forest is both hunted and
/// logged, a pasture is also a nomad's range. Territory, not tenure, so they are bit flags rather than one
/// id. The top three bits are free for plugins.
/// </summary>
[Flags]
public enum LandActivity : byte
{
    None = 0,
    /// <summary>Herds are driven across it: a nomadic or seasonal range, as against fenced <see cref="LandUseId.Pasture"/>.</summary>
    Grazing = 1,
    Hunting = 2,
    /// <summary>Gathering of wild fruit, roots, honey, fuel.</summary>
    Foraging = 4,
    Logging = 8,
    /// <summary>Fishing grounds: sea, lake or river tiles.</summary>
    Fishing = 16,
}

/// <summary>
/// What is actually done with a tile. A *decision* layer: Core never sets it by itself; the allocation
/// stages plugins and games add (see <see cref="LandCapacityStage"/>) decide it, and a hand-painted pin
/// (<see cref="UserEditFlags.PinLandUse"/>) wins over all of them. It is not a physics input: no climate,
/// erosion or hydrology stage reads it, so land use can never move a river or a biome.
/// </summary>
public struct LandUseInfo
{
    /// <summary>One of <see cref="LandUseId"/>, or a plugin id from 128 up.</summary>
    public byte Use;
    /// <summary>Bit set of <see cref="LandActivity"/> (plus plugin bits).</summary>
    public byte Activities;
    /// <summary>How hard the ground is worked, 0 (barely) to 255 (to its limit). Plugins may use it as a yield or depletion factor.</summary>
    public byte Intensity;
}

public class LandUseLayer : StoreLayer<LandUseInfo>
{
    public LandUseLayer(WorldDataStore store) : base(store) { }
}

/// <summary>
/// What a tile *can* support, 0 to 255 for each product, derived from the physics alone. It is the shared
/// answer to "where could people farm / herd / hunt / log / fish", so every allocator ranks the same ground
/// the same way (as <see cref="SettlementScoring"/> does for towns). It decides nothing: a desert tile
/// scores 0 for farmland but a plugin may still irrigate it.
/// </summary>
public struct LandCapacityInfo
{
    /// <summary>Rain-fed or irrigated tillage: warm enough, wet enough, flat, low, not salt or rock.</summary>
    public byte Arable;
    /// <summary>Grass for herds: wider than arable. Steppe, tundra and alpine meadow count.</summary>
    public byte Pasture;
    /// <summary>Huntable game: biomass and cover, from forest down to scrub.</summary>
    public byte Game;
    /// <summary>Standing timber.</summary>
    public byte Timber;
    /// <summary>Fish on sea, lake and river tiles. On the ocean it peaks on shallow shelves and cold, upwelling currents.</summary>
    public byte Fish;
}

public class LandCapacityLayer : StoreLayer<LandCapacityInfo>
{
    public LandCapacityLayer(WorldDataStore store) : base(store) { }
}
