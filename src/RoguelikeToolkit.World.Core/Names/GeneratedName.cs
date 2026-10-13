namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Rich name result: text plus full etymology metadata.</summary>
public sealed class GeneratedName
{
    /// <summary>The finished name as written, in the requested <see cref="NameScript"/>.</summary>
    public string Text { get; init; } = string.Empty;
    /// <summary>Race key the name was generated for, as passed in (blank is treated as <see cref="FantasyRaces.Human"/>).</summary>
    public string Race { get; init; } = FantasyRaces.Human;
    /// <summary>Culture key the name was generated for, as passed in (blank is treated as <see cref="Cultures.FantasyCommon"/>).</summary>
    public string Culture { get; init; } = Cultures.FantasyCommon;
    /// <summary>Normalized lineage the name follows: one of the <see cref="Lineages"/> values. For places this is the table's default.</summary>
    public string Lineage { get; init; } = Lineages.Patrilineal;
    /// <summary>Where the name's material comes from; see <see cref="NameProvenance"/>.</summary>
    public NameProvenance Provenance { get; init; } = new();
    /// <summary>The requested inspiration tokens that matched the morphemes actually used (empty when none were requested or none matched).</summary>
    public IReadOnlyList<string> Inspirations { get; init; } = Array.Empty<string>();
    /// <summary>The name broken into its parts in generation order (note that <see cref="Text"/> may reorder them, for example family name first).</summary>
    public IReadOnlyList<NamePart> Parts { get; init; } = Array.Empty<NamePart>();
    /// <summary>The seed passed to the generator (not the salted internal state); with the same inputs it reproduces this name.</summary>
    public int Seed { get; init; }
    /// <summary>Etymology line built from <see cref="Parts"/>, such as <c>Form “gloss” + Form “gloss”</c>. Glosses are always English.</summary>
    public string Gloss => string.Join(" + ", Parts.Select(p => $"{p.Form} “{p.Gloss}”"));
    /// <summary>Returns <see cref="Text"/>.</summary>
    public override string ToString() => Text;
}
