namespace RoguelikeToolkit.World.Core;

public enum BiomeType : byte
{
    Ocean = 0,
    Plains = 1,
    Desert = 2,
    Forest = 3,
    Mountain = 4,
    Tundra = 5,
    Jungle = 6,
    Swamp = 7,
    /// <summary>Land ice: ice caps and glaciated ranges.</summary>
    Glacier = 8,
    /// <summary>Arid slot gorge carved by a powerful river.</summary>
    Canyon = 9
}

public struct LocalMapInfo
{
    public uint Seed;
    public BiomeType Biome;
    public byte DangerLevel;
}
