using System;

namespace RoguelikeToolkit.World.Core;

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
