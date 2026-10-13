namespace RoguelikeToolkit.World.Core.Names;

[Preserve] // populated by YamlDotNet through reflection
public sealed class MorphemeYaml
{
    public string Form { get; set; } = string.Empty;
    /// <summary>
    /// Optional Latin transliteration of <see cref="Form"/> (Russian, Hebrew
    /// tables). Used only when generating with <see cref="NameScript.Latin"/>.
    /// </summary>
    public string Latin { get; set; } = string.Empty;
    public string Gloss { get; set; } = string.Empty;
    /// <summary>
    /// Free tags used for place fit (river, forest, ...) plus reserved markers:
    /// <c>masc</c>/<c>fem</c> (gendered given names and surname forms; <c>fem</c> on a place noun
    /// triggers <see cref="CultureData.PlaceMutation"/>),
    /// <c>solo</c> (never takes a second stem), <c>bound</c> (half a name: always takes a second stem),
    /// <c>nopatron</c> (not usable as a parent name).
    /// </summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>Stem to fuse suffixes onto (Russian "Пётр" -> "Петр" + "ович"). Blank uses Form.</summary>
    public string Base { get; set; } = string.Empty;
    public string BaseLatin { get; set; } = string.Empty;
    /// <summary>
    /// A descriptor's form after a feminine place noun, before any mutation, where it differs
    /// (Welsh gwyn -> gwen, melyn -> melen). Only read by tables with <see cref="CultureData.PlaceMutation"/>.
    /// </summary>
    public string FemForm { get; set; } = string.Empty;
}
