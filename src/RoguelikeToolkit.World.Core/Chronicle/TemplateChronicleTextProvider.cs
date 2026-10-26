using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// The deterministic Core default for <see cref="IChronicleTextProvider"/>: a weighted grammar in YAML
/// (<c>Chronicle/Data/Grammar</c>) over a lexicon (<c>Chronicle/Data/Lexicon</c>), each with a common file and one overlay per
/// culture; an event renders through the overlay of its acting polity's culture.
/// <list type="bullet">
/// <item>Every event kind has a root symbol <c>event.{Kind}</c> (<c>event.generic</c> when a kind has none); battles compose an
/// intro, the engagement, a turning point from the battle's beats, the fate of the generals, the rout, the aftermath and
/// sometimes a cry; sieges tell their course and their end.</item>
/// <item>Repetition control: the log renders once, in order, with a per-symbol memory of the rules drawn; a rule inside its
/// cooldown window is not drawn while another can be, and a lexicon word is used once per event.</item>
/// <item>Draws come from <c>HistoryRng.Create(seed, "Chronicle", turn, eventId, symbolPath)</c>, so the text is a pure
/// function of the log, its records, the names and the grammar; nothing is stored in the catalog, and rendering never
/// changes it.</item>
/// </list>
/// Because each event's text depends on the events before it, <see cref="Render"/> renders the whole log; the result is
/// cached for the catalog until its log, or a polity rename, changes. Thread-safe.
/// </summary>
public sealed class TemplateChronicleTextProvider : IChronicleTextProvider
{
    /// <summary>The fallback root for a kind without its own symbol.</summary>
    internal const string GenericRoot = "event.generic";

    private readonly ChronicleOptions _options;
    private readonly ChronicleData _data;
    private readonly object _gate = new();
    private WeakReference<HistoryCatalog>? _cachedCatalog;
    private CacheKey _cachedKey;
    private string[] _cachedTexts = Array.Empty<string>();

    private readonly record struct CacheKey(int Count, int FirstId, int LastId, int Seed, string Renames);

    /// <summary>Builds a provider over the shipped grammar (or <see cref="ChronicleOptions.DataDirectory"/>).</summary>
    /// <param name="options">Settings; null uses the defaults.</param>
    public TemplateChronicleTextProvider(ChronicleOptions? options = null)
    {
        _options = options ?? new ChronicleOptions();
        _data = _options.DataDirectory is null ? ChronicleData.Shared : ChronicleData.Load(_options.DataDirectory);
    }

    /// <summary>Builds a provider over explicit data (tests).</summary>
    internal TemplateChronicleTextProvider(ChronicleData data, ChronicleOptions? options = null)
    {
        _options = options ?? new ChronicleOptions();
        _data = data;
    }

    /// <summary>The grammar and lexicon in use.</summary>
    internal ChronicleData Data => _data;

    /// <inheritdoc />
    public string Render(HistoryCatalog catalog, int eventId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var texts = Texts(catalog);
        var events = catalog.Events;
        int lo = 0, hi = events.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int id = events[mid].Id;
            if (id == eventId) return texts[mid];
            if (id < eventId) lo = mid + 1;
            else hi = mid - 1;
        }
        return string.Empty;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RenderAll(HistoryCatalog catalog, int start = 0, int count = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Start cannot be negative.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        var texts = Texts(catalog);
        int from = Math.Min(start, texts.Length);
        int n = (int)Math.Min((long)count, texts.Length - from);
        var slice = new string[n];
        Array.Copy(texts, from, slice, 0, n);
        return slice;
    }

    private string[] Texts(HistoryCatalog catalog)
    {
        var key = KeyOf(catalog);
        lock (_gate)
        {
            if (_cachedCatalog != null && _cachedCatalog.TryGetTarget(out var cached) && ReferenceEquals(cached, catalog) && _cachedKey == key)
                return _cachedTexts;
            var texts = RenderLog(catalog, null);
            _cachedCatalog = new WeakReference<HistoryCatalog>(catalog);
            _cachedKey = key;
            _cachedTexts = texts;
            return texts;
        }
    }

    /// <summary>
    /// Renders the whole log in order. When <paramref name="drawn"/> is given it receives, per event, the rules drawn (for
    /// coverage tests).
    /// </summary>
    internal string[] RenderLog(HistoryCatalog catalog, List<ChronicleRule[]>? drawn)
    {
        var names = _options.Names ?? new DefaultChronicleNames();
        var builder = new ChronicleFactBuilder(catalog, names, _options);
        var expander = new ChronicleExpander(_data, new ChronicleMemory());
        var events = catalog.Events;
        var texts = new string[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var facts = builder.Build(e);
            string root = ChronicleFactBuilder.RootOf(e.Kind);
            string text = expander.Render(root, facts, catalog.Seed, e.Turn, e.Id);
            if (text.Length == 0) text = expander.Render(GenericRoot, facts, catalog.Seed, e.Turn, e.Id);
            texts[i] = text;
            drawn?.Add(expander.Drawn.ToArray());
        }
        return texts;
    }

    private static CacheKey KeyOf(HistoryCatalog catalog)
    {
        var events = catalog.Events;
        string renames = string.Empty;
        if (catalog.Overrides.Renames.Count > 0)
            renames = string.Join("\u0001", catalog.Overrides.Renames.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
        return new CacheKey(events.Count, events.Count > 0 ? events[0].Id : -1, events.Count > 0 ? events[events.Count - 1].Id : -1,
            catalog.Seed, renames);
    }
}
