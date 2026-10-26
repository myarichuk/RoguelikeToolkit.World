using System.Runtime.InteropServices.JavaScript;
using RoguelikeToolkit.World.Core.Chronicle;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.MapEditor.Engine;

/// <summary>
/// The chronicle (docs/HISTORY_EXPANSION_PLAN.md §6, X7): prose per event, rendered late from the log and the records, never
/// stored. The editor hands its own realm and person names over once (<see cref="ConfigureChronicleNames"/>) so the prose
/// agrees with its cards; anything it leaves out reads with the chronicle's own names.
/// </summary>
public static partial class WorldExports
{
    private static TemplateChronicleTextProvider? _chronicle;
    private static HistoryCatalog? _chronicleFor;
    private static ChronicleNameTable _chronicleNames = new();

    /// <summary>
    /// The host's names for the chronicle, by id: realm titles (<c>Kingdom of Varn</c>; a title of the form <c>X of Y</c> reads
    /// with <c>the</c> mid-sentence), realm base names (<c>Varn</c>, for later mentions), person names and settlement names.
    /// Blank or missing entries fall back to the chronicle's own. Applies to the next <see cref="HistoryChronicle"/>.
    /// </summary>
    [JSExport]
    public static void ConfigureChronicleNames(string[] realmTitles, string[] realmBases, string[] persons, string[] settlements)
    {
        var realms = realmTitles.Select(t => string.IsNullOrWhiteSpace(t) || t.StartsWith("the ", StringComparison.Ordinal) || !t.Contains(" of ", StringComparison.Ordinal)
            ? t : "the " + t).ToArray();
        _chronicleNames = new ChronicleNameTable(realms, realmBases, persons, settlements);
        _chronicle = null;
    }

    /// <summary>
    /// One page of the chronicle, aligned with <see cref="HistoryEvents"/> (one string per event, empty when an event has no
    /// text). The whole log renders once per world and names, then pages are slices of it.
    /// </summary>
    [JSExport]
    public static string[] HistoryChronicle(int start, int count)
    {
        var h = Current.History;
        if (_chronicle == null || !ReferenceEquals(_chronicleFor, h))
        {
            _chronicle = new TemplateChronicleTextProvider(new ChronicleOptions { TurnYears = Math.Max(1, _builtTurnYears), Names = _chronicleNames });
            _chronicleFor = h;
        }
        return _chronicle.RenderAll(h, start, count).ToArray();
    }
}
