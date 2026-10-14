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
    public double Pressure { get; set; }
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
}
