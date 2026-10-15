namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One long-life actor ruling a polity: agenda-driven, exempt from succession (see
/// <see cref="VillainOperator"/>, which freezes the polity's ruler generation at the rise turn),
/// and bounded by <see cref="HistoryContext.MaxVillains"/> plus the stage's own budget share.
/// </summary>
public sealed class VillainRecord
{
    /// <summary>Villain index in <see cref="HistoryCatalog.Villains"/>; used in event tags.</summary>
    public int Id { get; init; }
    /// <summary>Ruled polity id.</summary>
    public int PolityId { get; init; }
    /// <summary>Folk key of the villain (always a long-life folk).</summary>
    public string FolkKey { get; init; } = string.Empty;
    /// <summary>History turn the villain rose on.</summary>
    public int RisenTurn { get; init; }
    /// <summary>Agenda driving pressure bumps (<c>dominion</c>, <c>ruin</c>, <c>hunger</c>, <c>shadow</c>).</summary>
    public string Agenda { get; init; } = string.Empty;
}
