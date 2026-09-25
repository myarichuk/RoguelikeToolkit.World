using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Simplified thermal erosion: each iteration moves material from a tile to its
/// lowest neighbor wherever the slope exceeds the talus angle. In-place with a
/// fixed low-to-high tile order, so output stays deterministic for a given input.
/// Runs after elevation (15) and before biomes (20).
/// </summary>
[WorldGeneratorStage(17)]
public class ErosionGenerationStage : IWorldGeneratorStage
{
    public int Iterations { get; set; } = 10;
    public double Talus { get; set; } = 0.02;
    public double Rate { get; set; } = 0.25;

    public ErosionGenerationStage()
    {
    }

    public ErosionGenerationStage(int iterations = 10)
    {
        Iterations = iterations;
    }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();

        Span<int> neighbors = stackalloc int[6];

        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < store.TileCount; i++)
            {
                int adjacent = store.GetAdjacent(i, neighbors);
                int lowest = -1;
                float lowestHeight = elev[i].Height;

                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (elev[j].Height < lowestHeight)
                    {
                        lowestHeight = elev[j].Height;
                        lowest = j;
                    }
                }

                if (lowest < 0) continue;

                double slope = elev[i].Height - lowestHeight;
                if (slope > Talus)
                {
                    float move = (float)((slope - Talus) * Rate * 0.5);
                    elev[i].Height -= move;
                    elev[lowest].Height += move;
                }
            }
        }
    }
}
