using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Late renderer for the history log: turns tag-only <see cref="HistoryEvent"/>s (and the battle, person and settlement
/// records they point at) into prose. The simulation never reads the text, and a provider must never change the catalog, so
/// any provider (including one that returns empty strings) leaves <see cref="HistoryCatalog.ToBytes"/> identical.
/// <see cref="TemplateChronicleTextProvider"/> is the deterministic Core default; a game may supply its own (an LLM-backed
/// one, say) behind the same contract.
/// </summary>
public interface IChronicleTextProvider
{
    /// <summary>Renders the event whose <see cref="HistoryEvent.Id"/> is <paramref name="eventId"/>; empty when the log has no such event.</summary>
    /// <param name="catalog">The history to read.</param>
    /// <param name="eventId">The event's stable id (not its index in the log).</param>
    string Render(HistoryCatalog catalog, int eventId);

    /// <summary>
    /// Renders <paramref name="count"/> events of the log starting at index <paramref name="start"/> of
    /// <see cref="HistoryCatalog.Events"/>, one string per event in log order (clamped to the log).
    /// </summary>
    /// <param name="catalog">The history to read.</param>
    /// <param name="start">First log index.</param>
    /// <param name="count">Number of events; more than remain renders to the end.</param>
    IReadOnlyList<string> RenderAll(HistoryCatalog catalog, int start = 0, int count = int.MaxValue);
}
