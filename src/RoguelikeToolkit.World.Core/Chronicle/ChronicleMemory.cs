using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Repetition control across a rendered log: when each rule of each symbol, and each word of each lexicon list, was last
/// drawn. Recency is ranked among the alternatives actually competing for a draw: a rule is cooling while fewer than its
/// cooldown of the other candidates have been drawn since it was (by default half the candidates, so the most recent half
/// waits), and gets weight 0; when every candidate is cooling (only under explicit cooldowns), the least recently used one
/// wins. Draws made while an event renders stay pending until the event succeeds, so a branch the expander backs out of
/// leaves no trace. Pure bookkeeping: a sequence counter, no clock, no string hashing.
/// </summary>
internal sealed class ChronicleMemory
{
    private readonly Dictionary<string, Dictionary<string, long>> _rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, long>> _words = new(StringComparer.Ordinal);
    private readonly List<(string Symbol, string Id, long Seq)> _pending = new();
    private readonly Dictionary<string, int> _fullNames = new(StringComparer.Ordinal);
    private long _seq;
    private int _event;

    /// <summary>Events after which a name said in full is said in its short form (a chronicle introduces a realm once, then
    /// calls it by its short name while it stays in the story).</summary>
    public const int NameWindow = 6;

    /// <summary>Counts one more rendered event (the clock of <see cref="ShouldShorten"/>).</summary>
    public void NextEvent() => _event++;

    /// <summary>
    /// True when <paramref name="fullName"/> was said in full within the last <see cref="NameWindow"/> events, so this mention
    /// may use the short form; otherwise records that it is said in full now.
    /// </summary>
    public bool ShouldShorten(string fullName)
    {
        if (_fullNames.TryGetValue(fullName, out int at) && _event - at <= NameWindow) return true;
        _fullNames[fullName] = _event;
        return false;
    }

    /// <summary>A mark to roll pending rule draws back to.</summary>
    public int Mark => _pending.Count;

    /// <summary>Forgets pending rule draws made after <paramref name="mark"/>.</summary>
    public void Rollback(int mark)
    {
        if (mark < _pending.Count) _pending.RemoveRange(mark, _pending.Count - mark);
    }

    /// <summary>Notes a pending draw of <paramref name="rule"/>.</summary>
    public void Use(ChronicleRule rule) => _pending.Add((rule.Symbol, rule.Id, ++_seq));

    /// <summary>Makes the pending draws part of the history (the event rendered).</summary>
    public void Commit()
    {
        foreach (var (symbol, id, seq) in _pending)
        {
            if (!_rules.TryGetValue(symbol, out var map)) _rules[symbol] = map = new Dictionary<string, long>(StringComparer.Ordinal);
            map[id] = seq;
        }
        _pending.Clear();
    }

    /// <summary>Drops pending draws (the event failed to render).</summary>
    public void Discard() => _pending.Clear();

    /// <summary>When <paramref name="id"/> of <paramref name="symbol"/> was last drawn, counting pending draws, or -1 when never.</summary>
    public long LastUse(string symbol, string id)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
            if (string.Equals(_pending[i].Symbol, symbol, StringComparison.Ordinal) && string.Equals(_pending[i].Id, id, StringComparison.Ordinal))
                return _pending[i].Seq;
        return _rules.TryGetValue(symbol, out var map) && map.TryGetValue(id, out var seq) ? seq : -1;
    }

    /// <summary>
    /// Draws one of <paramref name="candidates"/>: by weight among those not cooling, else the least recently used (first in
    /// order on a tie).
    /// </summary>
    public int Choose(IReadOnlyList<ChronicleRule> candidates, ref Rng rng)
    {
        int n = candidates.Count;
        var last = new long[n];
        for (int i = 0; i < n; i++) last[i] = LastUse(candidates[i].Symbol, candidates[i].Id);
        var open = new bool[n];
        double total = 0;
        int oldest = 0;
        for (int i = 0; i < n; i++)
        {
            var r = candidates[i];
            int cd = r.Cooldown >= 0 ? r.Cooldown : ChronicleGrammar.DefaultCooldown(n);
            open[i] = last[i] < 0 || Rank(last, i) >= cd;
            if (open[i]) total += r.Weight;
            if (last[i] < last[oldest]) oldest = i;
        }
        double roll = rng.NextDouble();
        if (total <= 0) return oldest;
        double x = roll * total;
        int pick = -1;
        for (int i = 0; i < n; i++)
        {
            if (!open[i]) continue;
            pick = i;
            x -= candidates[i].Weight;
            if (x < 0) break;
        }
        return pick;
    }

    /// <summary>
    /// Draws a word of list <paramref name="key"/>: uniformly among those neither used in this event (<paramref name="used"/>)
    /// nor among the most recently drawn half of the list, else among those unused in this event, else the least recently
    /// drawn. Records the draw.
    /// </summary>
    public string ChooseWord(string key, string[] words, HashSet<string> used, ref Rng rng)
    {
        if (!_words.TryGetValue(key, out var map)) _words[key] = map = new Dictionary<string, long>(StringComparer.Ordinal);
        int n = words.Length;
        var last = new long[n];
        for (int i = 0; i < n; i++) last[i] = map.TryGetValue(words[i], out var s) ? s : -1;
        int cd = n / 2;
        var fresh = new List<int>(n);
        var unused = new List<int>(n);
        int oldest = 0;
        for (int i = 0; i < n; i++)
        {
            if (last[i] < last[oldest]) oldest = i;
            if (used.Contains(words[i])) continue;
            unused.Add(i);
            if (last[i] < 0 || Rank(last, i) >= cd) fresh.Add(i);
        }
        double roll = rng.NextDouble();
        int pick = fresh.Count > 0 ? fresh[(int)(roll * fresh.Count)]
            : unused.Count > 0 ? unused[(int)(roll * unused.Count)]
            : oldest;
        string word = words[pick];
        map[word] = ++_seq;
        used.Add(word);
        return word;
    }

    /// <summary>How many of the other entries were drawn after entry <paramref name="i"/>.</summary>
    private static int Rank(long[] last, int i)
    {
        int rank = 0;
        for (int j = 0; j < last.Length; j++)
            if (j != i && last[j] > last[i]) rank++;
        return rank;
    }
}
