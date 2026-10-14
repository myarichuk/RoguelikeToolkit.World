namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>Pairwise offspring rules loaded from <c>Peoples/Data/compat.yaml</c> (see <see cref="PeoplesStore"/>).</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class CompatData
{
    /// <summary>One rule row per incompatible (or explicitly ruled) folk pair.</summary>
    public List<CompatRule> Rules { get; set; } = new();

    /// <summary>One compat row: two folk keys and their rule.</summary>
    [Preserve] // populated by YamlDotNet through reflection
    public sealed class CompatRule
    {
        /// <summary>First folk key; required non-blank.</summary>
        public string FolkA { get; set; } = string.Empty;
        /// <summary>Second folk key; required non-blank and different from <see cref="FolkA"/>.</summary>
        public string FolkB { get; set; } = string.Empty;
        /// <summary>Rule: <c>blend</c>, <c>dominant</c> or <c>sterile</c> (validated at load, case-insensitive).</summary>
        public string Rule { get; set; } = string.Empty;
    }
}
