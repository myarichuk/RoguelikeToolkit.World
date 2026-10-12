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
    // Ids 10, 11 and 13 are left free on purpose: the MapEditor ships paint-only biomes there
    // (Savanna, Farmland, Ashlands). Badlands deliberately shares the editor's id 12.
    /// <summary>Hot arid ground carved into dissected, banded gullies and mesas.</summary>
    Badlands = 12,
    /// <summary>Hot arid stony plateau (hamada, reg): scoured bedrock and desert pavement, little sand.</summary>
    RockDesert = 14,
    /// <summary>Dry endorheic basin floor: salt pan, playa, sabkha.</summary>
    SaltFlat = 15,
    /// <summary>Dry but cold steppe-desert (Gobi, Patagonia): too cold to be hot desert, too dry to be grassland.</summary>
    ColdDesert = 16
}

public struct LocalMapInfo
{
    public BiomeType Biome;
    public byte DangerLevel;
}
