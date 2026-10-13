namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Where a river's water ends up.</summary>
public enum RiverTerminal : byte
{
    /// <summary>Reaches the sea (exorheic).</summary>
    Sea = 0,
    /// <summary>Ends in a lake (open or endorheic).</summary>
    Lake = 1,
    /// <summary>Dies inland: desert sink or playa (endorheic).</summary>
    Sink = 2
}
