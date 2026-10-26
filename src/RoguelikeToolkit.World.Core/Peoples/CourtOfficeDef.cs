namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>One office of a court (see <see cref="CourtDef.Forms"/>).</summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class CourtOfficeDef
{
    /// <summary>Office name (<c>chancellor</c>, <c>marshal</c>...); one holder per polity.</summary>
    public string Office { get; set; } = string.Empty;
    /// <summary>Person kind of the holder (<c>Advisor</c>, <c>General</c>, <c>Spymaster</c>, <c>MerchantPrince</c>...).</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Skill a candidate is judged by (a key of <see cref="CourtDef.Skills"/>).</summary>
    public string Skill { get; set; } = string.Empty;
}
