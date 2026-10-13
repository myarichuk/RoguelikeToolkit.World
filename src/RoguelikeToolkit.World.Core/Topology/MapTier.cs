namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>Tier of a map in the planet -&gt; region -&gt; local hierarchy.</summary>
public enum MapTier : byte
{
    /// <summary>A tile of the world's icosahedral hex grid, stored in a <see cref="Storage.WorldDataStore"/>.</summary>
    Planet = 0,
    /// <summary>The square patch of cells that subdivides one planet tile.</summary>
    Region = 1,
    /// <summary>The square patch of tiles that subdivides one region cell.</summary>
    Local = 2,
}
