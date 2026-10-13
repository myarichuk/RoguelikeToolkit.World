using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public struct LocalTile
{
    public int Index;
    public float Height;
    public BiomeType Biome;
    public byte Danger;
    public bool IsWater;
    public float Temperature;
    public float Precipitation;
    public float WaterDepth;
    public BiomeType ParentBiome;
}
