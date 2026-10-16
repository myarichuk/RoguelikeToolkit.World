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
            "fall" when parts.Length > 2 && parts[1] == "self" => $"In the {faithForm} telling, the realm broke in turn {parts[2]}, and the power of {domain} turned its face away.",
            "fall" when parts.Length > 2 && parts[1] == "parent" => $"The {faithForm} faithful hold that the old realm fell in turn {parts[2]}, and that they are the remnant {domain} kept alive.",
            "absorbed" when parts.Length > 2 && parts[1] == "self" => $"The {faithForm} faithful were taken into a greater realm in turn {parts[2]}, and the power of {domain} went with them.",
            "absorbed" when parts.Length > 2 && parts[1] == "into" => $"In the {faithForm} telling, the realm swallowed a lesser state in turn {parts[2]}, and the power of {domain} blessed the feast.",
            _ => string.Empty,
        };
    }
}
