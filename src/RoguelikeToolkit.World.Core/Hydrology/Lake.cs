namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>One resolved lake: ponded tiles, surface level, and outflow state.</summary>
public sealed class Lake
{
    /// <summary>Index of the lake in the solver's result (0-based); not the same numbering as <see cref="HydrologyInfo.WaterBodyId"/>.</summary>
    public int Id { get; }
    /// <summary>Tile indices ponded by the lake.</summary>
    public List<int> Tiles { get; } = new();
    /// <summary>Water surface height. Tiles of the lake are routed at this level.</summary>
    public float Surface { get; set; }
    /// <summary>True when the lake fills its whole basin and overflows downstream through <see cref="Outlet"/>; false for a closed lake that loses water only by evaporation.</summary>
    public bool IsOpen { get; set; }
    /// <summary>Total evaporation loss over the ponded tiles, in the same runoff units as <see cref="Supply"/>.</summary>
    public float EvapTotal { get; set; }
    /// <summary>Outlet tile (an in-lake tile draining out); -1 for closed lakes.</summary>
    public int Outlet { get; set; } = -1;

    /// <summary>Water reaching the lake per unit time: its basin's outside inflow plus the runoff its own tiles catch.</summary>
    public float Supply { get; set; }

    /// <summary>
    /// Share of the inflow that leaves through the outlet instead of evaporating (0 for a closed lake).
    /// Dissolved salt only leaves with the outflow, so the brine concentrates to about 1 / flushing times
    /// the inflow's salinity.
    /// </summary>
    public float Flushing => IsOpen && Supply > 0f ? Math.Max(0f, (Supply - EvapTotal) / Supply) : 0f;

    /// <summary>Concentration factor above which a lake reads as salty: a lake that evaporates two thirds of what it receives.</summary>
    public const float SalineConcentration = 3f;

    /// <summary>
    /// Terminal lakes are salt lakes (Dead Sea, Great Salt Lake), and so are open ones that evaporate most of
    /// their inflow and spill little (Lake Chad, Titicaca's brackish south). A lake that flushes freely stays fresh.
    /// </summary>
    public bool IsSaline => !IsOpen || Flushing * SalineConcentration < 1f;

    /// <summary>Creates an empty lake.</summary>
    /// <param name="id">Value for <see cref="Id"/>.</param>
    public Lake(int id) => Id = id;
}
