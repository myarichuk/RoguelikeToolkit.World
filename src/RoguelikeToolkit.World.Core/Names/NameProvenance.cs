namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Describes where a generated name's material comes from.</summary>
public sealed class NameProvenance
{
    /// <summary>Source category; the generator always sets <c>original</c>.</summary>
    public string Inspiration { get; init; } = "original";
    /// <summary>Free-text note: the table's <see cref="CultureData.Provenance"/>, for places prefixed with the kind and nearby terrain.</summary>
    public string Note { get; init; } = string.Empty;
}
