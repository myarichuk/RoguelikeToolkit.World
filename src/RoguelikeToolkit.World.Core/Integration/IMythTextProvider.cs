namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Late renderer for faith myths. History stores only tags (<c>origin:{folk}</c>,
/// <c>trauma:war:{turn}</c>, <c>fall:self|parent:{turn}</c>, <c>absorbed:self|into:{turn}</c>, ...); a game may supply an LLM-backed provider, while
/// <see cref="TemplateMythTextProvider"/> is the deterministic Core default. The simulation
/// never reads the returned text, so any provider (including one returning empty strings) leaves
/// the event log identical.
/// </summary>
public interface IMythTextProvider
{
    /// <summary>Renders one myth slot of a faith into text.</summary>
    /// <param name="faithForm">Faith form (animism, ancestor, polytheism, ...).</param>
    /// <param name="slot">Myth slot tag, e.g. <c>origin:dwarf</c> or <c>trauma:war:7</c>.</param>
    /// <param name="powers">The faith's power domains, as context.</param>
    /// <returns>Rendered text; may be empty.</returns>
    string Render(string faithForm, string slot, IReadOnlyList<string> powers);
}
