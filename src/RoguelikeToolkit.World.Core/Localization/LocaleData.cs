namespace RoguelikeToolkit.World.Core.Localization;

/// <summary>One UI language table (flat key/value strings plus layout direction).</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class LocaleData
{
    public string Language { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool RightToLeft { get; set; }
    public Dictionary<string, string> Strings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
