namespace RoguelikeToolkit.World.Core.Features;

/// <summary>
/// Sparse feature: one river as an ordered downstream tile chain. The path's
/// last tile is the terminal water tile (sea/lake) or the final land tile for
/// sink rivers.
/// </summary>
public sealed class River
{
    public int Id { get; }
    public List<int> Path { get; } = new();
    public RiverTerminal Terminal { get; set; } = RiverTerminal.Sea;
    /// <summary>Water-body id of the terminal sea/lake; -1 for sinks.</summary>
    public int TerminalBody { get; set; } = -1;
    /// <summary>Discharge at the mouth (last river tile).</summary>
    public float MouthFlow { get; set; }

    public River(int id) => Id = id;
}
