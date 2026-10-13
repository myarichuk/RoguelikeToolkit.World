namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>Tier of a map in the planet -&gt; region -&gt; local hierarchy.</summary>
public enum MapTier : byte
{
    Planet = 0,
    Region = 1,
    Local = 2,
}
