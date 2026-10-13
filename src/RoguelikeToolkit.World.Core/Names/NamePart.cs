namespace RoguelikeToolkit.World.Core.Names;

/// <summary>One component of a generated name (for example the given name or the patronymic) with its meaning.</summary>
public sealed class NamePart
{
    /// <summary>The part as written in the name, in the requested script.</summary>
    public string Form { get; init; } = string.Empty;
    /// <summary>English meaning of the part.</summary>
    public string Gloss { get; init; } = string.Empty;
    /// <summary>What the part is; one of the <see cref="NamePartRoles"/> values.</summary>
    public string Role { get; init; } = NamePartRoles.Given;
}
