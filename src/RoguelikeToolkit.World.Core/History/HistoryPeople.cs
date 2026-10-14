namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One (folk, culture) pair the world's history draws on. Seeded by <see cref="CultureSeedStage"/>
/// from <see cref="HistoryContext.Peoples"/>; <see cref="PolityFormStage"/> assigns each polity one
/// pair by deterministic draw. An ethnos (folk plus current culture plus deltas) is a P1 concept;
/// this roster only says which peoples exist to found polities.
/// </summary>
public sealed class HistoryPeople
{
    /// <summary>Key of a <c>FolkDef</c> (e.g. <c>human</c>).</summary>
    public string FolkKey { get; init; } = string.Empty;
    /// <summary>Key of a <c>CultureDef</c> (e.g. <c>human_common</c>).</summary>
    public string CultureKey { get; init; } = string.Empty;
}
