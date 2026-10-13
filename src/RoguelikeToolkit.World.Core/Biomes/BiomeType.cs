using RoguelikeToolkit.World.Core.Climate;

namespace RoguelikeToolkit.World.Core.Biomes;

/// <summary>Biome of a planet tile, stored as a byte. Ids are stable (some match the editor's colour slots); gaps are retired or reserved ids.</summary>
public enum BiomeType : byte
{
    /// <summary>Open water: any tile below <see cref="Elevation.ElevationGenerationStage.SeaLevel"/>. Glacier and canyon overrides never replace it.</summary>
    Ocean = 0,
    /// <summary>Default temperate land: moist enough to be neither desert nor tundra, too dry for forest, and claimed by no other rule.</summary>
    Plains = 1,
    /// <summary>Hot arid lowland: sand seas and dunes (erg). The other arid types are the rest of the family.</summary>
    Desert = 2,
    /// <summary>Land wetter than <see cref="BiomeClassifier.ForestMinMoisture"/> that no hotter, wetter or seasonal rule claimed.</summary>
    Forest = 3,
    /// <summary>High ground above <see cref="BiomeClassifier.MountainHeight"/> that is not ice-covered (a high, cold, wet tile is <see cref="Glacier"/> instead).</summary>
    Mountain = 4,
    /// <summary>Cold land below <see cref="BiomeClassifier.TundraMaxTemperature"/>.</summary>
    Tundra = 5,
    /// <summary>Hot, wet land: warmer than <see cref="BiomeClassifier.JungleMinTemperature"/> and wetter than <see cref="BiomeClassifier.JungleMinMoisture"/>.</summary>
    Jungle = 6,
    /// <summary>Warm waterlogged land: wetter than <see cref="BiomeClassifier.SwampMinMoisture"/> but not hot enough for <see cref="Jungle"/>.</summary>
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
