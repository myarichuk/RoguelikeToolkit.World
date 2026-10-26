namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Where a river's water ends up.</summary>
public enum RiverTerminal : byte
{
    /// <summary>Reaches the sea (exorheic).</summary>
    Sea = 0,
    /// <summary>Ends in a lake (open or endorheic).</summary>
    Lake = 1,
    /// <summary>Dies inland: desert sink or playa (endorheic).</summary>
    Sink = 2,
    /// <summary>Dries out on its way: the channel fades below river size on ground that still drains to the sea (exorheic).</summary>
    DriesOut = 3
}
