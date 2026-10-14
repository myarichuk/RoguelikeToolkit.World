namespace RoguelikeToolkit.World.Core.Lineage;

/// <summary>One folk share of an actor's lineage (<c>Folk</c> key, <c>Share</c> fraction in (0, 1]; see <see cref="Ancestry"/>).</summary>
/// <param name="Folk">Folk key; must be non-blank (checked by <see cref="Ancestry"/>).</param>
/// <param name="Share">Fraction of lineage, in (0, 1] (checked by <see cref="Ancestry"/>).</param>
public readonly record struct AncestryShare(string Folk, double Share);
