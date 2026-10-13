namespace RoguelikeToolkit.World.Core.Features;

/// <summary>
/// Sparse feature: one river as an ordered downstream tile chain. The path's
/// last tile is the terminal water tile (sea/lake) or the final land tile for
/// sink rivers.
/// </summary>
public sealed class River
{
    /// <summary>Catalog id; rivers are numbered in descending order of head discharge, ties broken by lower head tile index.</summary>
    public int Id { get; }
    /// <summary>Tile indices in flow order, from the head downstream; always at least two tiles when built by the hydrology stage. A river that ends in the sea or a lake includes that water tile, which is not flagged as a river reach in <see cref="Hydrology.HydrologyInfo"/>. A tributary stops on the tile before its confluence, so it does not include the tile of the river it joins.</summary>
    public List<int> Path { get; } = new();
    /// <summary>Where the water ends up. A tributary reports the terminal of the river it joins; see also <see cref="TerminalBody"/>.</summary>
    public RiverTerminal Terminal { get; set; } = RiverTerminal.Sea;
    /// <summary>Water-body id of the terminal sea/lake; -1 for sinks.</summary>
    public int TerminalBody { get; set; } = -1;
    /// <summary>Discharge at the mouth (last river tile).</summary>
    public float MouthFlow { get; set; }

    /// <summary>Creates a river with an empty path, terminal <see cref="RiverTerminal.Sea"/> and no terminal body.</summary>
    public River(int id) => Id = id;
}
