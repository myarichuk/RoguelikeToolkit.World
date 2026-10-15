namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One polity: a named-by-id actor on the map with a government form, a capital tile,
/// a folk and a culture. Identity is the <see cref="Id"/> (its index in
/// <see cref="HistoryCatalog.Polities"/>); display names arrive late via the
/// <see cref="HistoryOverrides"/> rename layer and the <c>Names</c> leaf renderer.
/// </summary>
public sealed class Polity
{
    /// <summary>Index in <see cref="HistoryCatalog.Polities"/>; tile ownership stores this value.</summary>
    public int Id { get; init; }
    /// <summary>How the polity holds together.</summary>
    public PolityForm Form { get; init; }
    /// <summary>Planet tile of the capital; the seed of this polity's territory.</summary>
    public int CapitalTile { get; init; }
    /// <summary>Key of the founding folk (a <c>FolkDef</c> key, e.g. <c>human</c>).</summary>
    public string FolkKey { get; init; } = string.Empty;
    /// <summary>Key of the founding culture (a <c>CultureDef</c> key, e.g. <c>human_common</c>).</summary>
    public string CultureKey { get; init; } = string.Empty;
    /// <summary>History turn the polity was founded on (0 for the first polities).</summary>
    public int FoundedTurn { get; init; }
    /// <summary>
    /// Abstract population index (people-equivalents, not a census): grown by
    /// <c>PopulationStage</c> from the territory's carrying capacity. Wars and conquests read it,
    /// never a name or a table.
    /// </summary>
    public double Population { get; set; }
    /// <summary>
    /// Crowding pressure 0..<c>HistoryDemography.MaxPressure</c>: how far the population has pushed
    /// past comfortable density, plus war and road-cut bumps. Drives war, conquest and rebellion.
    /// </summary>
    public double Pressure
    {
        get => GetScalar(ScalarRegistry.Pressure);
        set => SetScalar(ScalarRegistry.Pressure, value);
    }

    private double[] _scalars = Array.Empty<double>();

    /// <summary>Reads a declared scalar; an unset one reads as its declared default.</summary>
    public double GetScalar(int id, ScalarRegistry? registry = null)
    {
        if (id < _scalars.Length) return _scalars[id];
        return (registry ?? ScalarRegistry.Builtin)[id].Default;
    }

    /// <summary>Writes a declared scalar, clamped to its declared range.</summary>
    public void SetScalar(int id, double value, ScalarRegistry? registry = null)
    {
        var reg = registry ?? ScalarRegistry.Builtin;
        if (id >= _scalars.Length)
        {
            int old = _scalars.Length;
            Array.Resize(ref _scalars, Math.Max(id + 1, reg.Count));
            for (int i = old; i < _scalars.Length; i++) _scalars[i] = reg[i].Default;
        }
        _scalars[id] = reg[id].Clamp(value);
    }
    /// <summary>
    /// Winning culture key after a conquest, or empty when the polity renders through its founding
    /// <see cref="CultureKey"/>. Set by <c>ConquestStage</c>; the <c>Names</c> leaf resolves it late.
    /// </summary>
    public string CultureOverlay { get; set; } = string.Empty;
    /// <summary>How many successions have ticked (one per folk generation); advanced by <c>PopulationStage</c>.</summary>
    public int RulerGeneration { get; set; }
    /// <summary>
    /// Accumulated openness drift from road-volume exchange (-<c>HistoryDemography.MaxOpennessDrift</c>
    /// to +<c>HistoryDemography.MaxOpennessDrift</c>); advanced by <c>ExchangeStage</c>.
    /// </summary>
    public int OpennessDrift { get; set; }
    /// <summary>
    /// History turn the treaty heir was born on, or -1 when no marriage treaty has produced one.
    /// Set by <c>TreatyStage</c>; foster treaties admit the heir only once adult (see
    /// <c>TreatyStage.IsHeirAdult</c>), so child fostering is refused with a grudge.
    /// </summary>
    public int HeirBornTurn { get; set; } = -1;
    /// <summary>True once a <c>VillainStage</c> villain rose to rule this polity; successions freeze at the rise turn.</summary>
    public bool HasVillain { get; set; }
    /// <summary>History turn the polity ended (collapsed, absorbed), or -1 while alive. Unused by the ported operators in M1.</summary>
    public int EndedTurn { get; set; } = -1;
    /// <summary>Interned end cause (<see cref="StringTable"/>), or -1 while alive.</summary>
    public int EndCause { get; set; } = -1;
    /// <summary>Id of the polity this one split from, or -1 for a founding polity.</summary>
    public int Parent { get; set; } = -1;
    /// <summary>True until <see cref="EndedTurn"/> is set.</summary>
    public bool Alive => EndedTurn < 0;
}
