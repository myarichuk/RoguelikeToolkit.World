namespace RoguelikeToolkit.World.Core.Names;

/// <summary>One entry in a UI "style" picker: a race+culture pair resolved to a single table.</summary>
public sealed record NameStyle(string Race, string Culture, string TableKey, string Label);
