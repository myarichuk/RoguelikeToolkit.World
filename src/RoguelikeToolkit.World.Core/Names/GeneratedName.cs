namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Rich name result: text plus full etymology metadata.</summary>
public sealed class GeneratedName
{
    public string Text { get; init; } = string.Empty;
    public string Race { get; init; } = FantasyRaces.Human;
    public string Culture { get; init; } = Cultures.FantasyCommon;
    public string Lineage { get; init; } = Lineages.Patrilineal;
    public NameProvenance Provenance { get; init; } = new();
    public IReadOnlyList<string> Inspirations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NamePart> Parts { get; init; } = Array.Empty<NamePart>();
    public int Seed { get; init; }
    public string Gloss => string.Join(" + ", Parts.Select(p => $"{p.Form} “{p.Gloss}”"));
    public override string ToString() => Text;
}
