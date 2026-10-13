namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>One resolved lake: ponded tiles, surface level, and outflow state.</summary>
public sealed class Lake
{
    public int Id { get; }
    public List<int> Tiles { get; } = new();
    public float Surface { get; set; }
    public bool IsOpen { get; set; }
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

    public Lake(int id) => Id = id;
}
