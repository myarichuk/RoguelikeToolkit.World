namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// One epithet and when a person earns it: every <see cref="Min"/> entry at or below the person's value and every
/// <see cref="Max"/> entry at or above it. Keys are personality traits (<c>Openness</c>...<c>Neuroticism</c>) or skills
/// (<c>Martial</c>...), all on a 0-100 scale.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class EpithetDef
{
    /// <summary>Epithet name (an interned id in the history; prose renders it per culture).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Lower bounds.</summary>
    public Dictionary<string, double> Min { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Upper bounds.</summary>
    public Dictionary<string, double> Max { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
