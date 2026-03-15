using System;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(20)]
public class LocalMapGenerationStage : IWorldGeneratorStage
{
    public int Seed { get; set; } = 42;

    public LocalMapGenerationStage()
    {
    }

    public LocalMapGenerationStage(int seed = 42)
    {
        Seed = seed;
    }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var span = store.GetSpan<LocalMapInfo>();

        uint state = (uint)Seed;
        if (state == 0) state = 1;

        uint NextRandom()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        for (int i = 0; i < span.Length; i++)
        {
            span[i] = new LocalMapInfo
            {
                Seed = NextRandom(),
                Biome = BiomeType.Ocean,
                DangerLevel = 0
            };
        }
    }
}
