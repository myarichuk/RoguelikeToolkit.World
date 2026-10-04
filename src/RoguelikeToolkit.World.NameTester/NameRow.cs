namespace RoguelikeToolkit.World.NameTester;

/// <summary>One generated name as shown in the results and shortlist lists.</summary>
/// <param name="Warning">NameLint problems, empty when the name reads cleanly.</param>
public sealed record NameRow(string Text, string Gloss, string Meta, string Warning)
{
    public bool HasWarning => Warning.Length > 0;
}
