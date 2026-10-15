namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>Deterministic template renderer for myth slots: the Core default for <see cref="IMythTextProvider"/>.</summary>
public sealed class TemplateMythTextProvider : IMythTextProvider
{
    /// <inheritdoc />
    public string Render(string faithForm, string slot, IReadOnlyList<string> powers)
    {
        if (string.IsNullOrWhiteSpace(slot)) return string.Empty;
        string[] parts = slot.Split(':');
        string domain = powers is { Count: > 0 } ? powers[0] : "fate";
        return parts[0] switch
        {
            "origin" when parts.Length > 1 => $"In the {faithForm} telling, the {parts[1]} folk were first shaped under the sign of {domain}.",
            "trauma" when parts.Length > 2 => $"The {parts[1]} of turn {parts[2]} is remembered as a wound the {faithForm} faithful never forgave.",
            _ => string.Empty,
        };
    }
}
