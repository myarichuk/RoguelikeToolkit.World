namespace RoguelikeToolkit.World.Core;

public enum BiomeType : byte
{
    Ocean = 0,
    Plains = 1,
    /// <summary>Hot arid lowland: sand seas and dunes (erg). The other arid types are the rest of the family.</summary>
    Desert = 2,
    Forest = 3,
    Mountain = 4,
    Tundra = 5,
    Jungle = 6,
    Swamp = 7,
    /// <summary>Land ice: ice caps and glaciated ranges.</summary>
    Glacier = 8,
    /// <summary>Arid slot gorge carved by a powerful river.</summary>
    Canyon = 9,
    /// <summary>
    /// Warm grassland with a dry season: hot enough for savanna, wet enough not to be desert, and with
    /// rain that arrives in one season (see <see cref="ClimateInfo.RainSeasonality"/>). It takes the editor's
    /// old paint-only Savanna id, so a painted savanna is now a real biome.
    /// </summary>
    Savanna = 10,
    // Ids 11 and 13 are the editor's old Farmland/Ashlands colour slots; those are land uses now (see
    // LandUseId), not biomes. Badlands deliberately shares the editor's id 12.
    /// <summary>Hot arid ground carved into dissected, banded gullies and mesas.</summary>
    Badlands = 12,
    /// <summary>Hot arid stony plateau (hamada, reg): scoured bedrock and desert pavement, little sand.</summary>
    RockDesert = 14,
    /// <summary>Dry endorheic basin floor: salt pan, playa, sabkha.</summary>
    SaltFlat = 15,
    /// <summary>Dry but cold steppe-desert (Gobi, Patagonia): too cold to be hot desert, too dry to be grassland.</summary>
    ColdDesert = 16,
    /// <summary>
    /// Warm-temperate scrub and open woodland with a dry summer (maquis, chaparral, fynbos): the rain falls
    /// in winter, when the storm tracks have retreated towards the equator, and the summer belongs to the
    /// subtropical high (see <see cref="ClimateInfo.RainPhase"/>). The mirror of <see cref="Savanna"/>, whose
    /// rain comes in summer.
    /// </summary>
    // 17 and 18 are the editor's Pasture and Built-up colour slots (see LandUseId), so the next free id is 19.
    Mediterranean = 19
}

public struct LocalMapInfo
{
    public BiomeType Biome;
    public byte DangerLevel;
}
