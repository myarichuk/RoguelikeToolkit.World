namespace RoguelikeToolkit.World.Core.Names;

public sealed class NamePart
{
    public string Form { get; init; } = string.Empty;
    public string Gloss { get; init; } = string.Empty;
    public string Role { get; init; } = NamePartRoles.Given;
}
