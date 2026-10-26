using System.Globalization;
using System.Text;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Expands one event's root symbol into text. A rule is a candidate when its predicates hold and every slot it names
/// directly resolves; the draw goes through <see cref="ChronicleMemory"/>. A rule whose nested symbol cannot be expanded is
/// backed out and the next candidate tried, so an event renders whenever any path through the grammar fits its facts.
/// Every draw comes from <c>HistoryRng.Create(seed, "Chronicle", turn, eventId, symbolPath)</c>: the path names the symbol
/// chain and the occurrence, so one choice never shifts another's stream.
/// </summary>
internal sealed class ChronicleExpander
{
    /// <summary>Deepest nesting of symbols and lexicon words.</summary>
    public const int MaxDepth = 16;

    private const string Stage = "Chronicle";

    private readonly ChronicleData _data;
    private readonly ChronicleMemory _memory;
    private readonly HashSet<string> _usedWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mentioned = new(StringComparer.Ordinal);
    private ChronicleFacts _facts = new();
    private ChronicleGrammar _grammar = null!;
    private int _seed, _turn, _eventId;

    /// <summary>Builds an expander over <paramref name="data"/> that remembers its draws in <paramref name="memory"/>.</summary>
    public ChronicleExpander(ChronicleData data, ChronicleMemory memory)
    {
        _data = data;
        _memory = memory;
    }

    /// <summary>When set, predicates are ignored (lint: every rule of every symbol must render with full facts).</summary>
    public bool IgnorePredicates { get; set; }

    /// <summary>Rules drawn by the last <see cref="Render(string, ChronicleFacts, int, int, int)"/>, in order (for coverage tests).</summary>
    public List<ChronicleRule> Drawn { get; } = new();

    /// <summary>
    /// Renders <paramref name="root"/> for one event; returns the tidied text, or an empty string when no rule fits. The
    /// memory keeps the draws only when it succeeds.
    /// </summary>
    public string Render(string root, ChronicleFacts facts, int seed, int turn, int eventId)
        => Render(root, null, facts, seed, turn, eventId);

    /// <summary>Like <see cref="Render(string, ChronicleFacts, int, int, int)"/>, but with the root's rule forced (lint).</summary>
    public string Render(string root, ChronicleRule? forced, ChronicleFacts facts, int seed, int turn, int eventId)
    {
        _facts = facts;
        _grammar = _data.For(facts.Culture);
        _seed = seed;
        _turn = turn;
        _eventId = eventId;
        _usedWords.Clear();
        _occurrences.Clear();
        _mentioned.Clear();
        _memory.NextEvent();
        Drawn.Clear();
        var sb = new StringBuilder(256);
        bool ok = forced != null
            ? ExpandRule(forced, root, 0, sb)
            : ExpandSymbol(root, string.Empty, 0, sb);
        if (!ok)
        {
            _memory.Discard();
            return string.Empty;
        }
        _memory.Commit();
        return ChronicleText.Tidy(sb.ToString());
    }

    private string NextPath(string parent, string name)
    {
        string basePath = parent.Length == 0 ? name : parent + "/" + name;
        int n = _occurrences.TryGetValue(basePath, out var c) ? c : 0;
        _occurrences[basePath] = n + 1;
        return n == 0 ? basePath : basePath + "#" + n.ToString(CultureInfo.InvariantCulture);
    }

    private bool ExpandSymbol(string symbol, string parentPath, int depth, StringBuilder sb)
    {
        if (depth > MaxDepth) return false;
        if (!_grammar.TryRules(symbol, out var rules) || rules.Length == 0) return false;
        string path = NextPath(parentPath, symbol);
        var candidates = new List<ChronicleRule>(rules.Length);
        foreach (var r in rules)
            if ((IgnorePredicates || r.Matches(_facts)) && SlotsResolve(r.Template)) candidates.Add(r);
        if (candidates.Count == 0) return false;
        var rng = HistoryRng.Create(_seed, Stage, _turn, _eventId, path);
        while (candidates.Count > 0)
        {
            int pick = _memory.Choose(candidates, ref rng);
            var rule = candidates[pick];
            int mark = _memory.Mark, drawn = Drawn.Count, length = sb.Length;
            _memory.Use(rule);
            Drawn.Add(rule);
            if (ExpandTemplate(rule.Template, path, depth, sb)) return true;
            _memory.Rollback(mark);
            Drawn.RemoveRange(drawn, Drawn.Count - drawn);
            sb.Length = length;
            candidates.RemoveAt(pick);
        }
        return false;
    }

    private bool ExpandRule(ChronicleRule rule, string root, int depth, StringBuilder sb)
    {
        string path = NextPath(string.Empty, root);
        _memory.Use(rule);
        Drawn.Add(rule);
        return ExpandTemplate(rule.Template, path, depth, sb);
    }

    private bool SlotsResolve(ChronicleTemplate template)
    {
        foreach (var part in template.Parts)
            if (part.Kind == ChronicleTemplate.PartKind.Slot && !_facts.Has(part.Text) && !_grammar.HasWords(part.Text)) return false;
        return true;
    }

    private bool ExpandTemplate(ChronicleTemplate template, string path, int depth, StringBuilder sb)
    {
        foreach (var part in template.Parts)
        {
            switch (part.Kind)
            {
                case ChronicleTemplate.PartKind.Literal:
                    sb.Append(part.Text);
                    break;
                case ChronicleTemplate.PartKind.Slot:
                {
                    if (!TryResolveSlot(part.Text, path, depth, out var text)) return false;
                    sb.Append(Modify(text, part.Modifiers));
                    break;
                }
                default:
                {
                    var inner = new StringBuilder(64);
                    int mark = _memory.Mark, drawn = Drawn.Count;
                    if (ExpandSymbol(part.Text, path, depth + 1, inner)) sb.Append(Modify(inner.ToString(), part.Modifiers));
                    else if (part.Optional) { _memory.Rollback(mark); Drawn.RemoveRange(drawn, Drawn.Count - drawn); }
                    else return false;
                    break;
                }
            }
        }
        return true;
    }

    private static string Modify(string text, string[] modifiers)
    {
        foreach (var m in modifiers) text = ChronicleText.Apply(text, m);
        return text;
    }

    private bool TryResolveSlot(string name, string path, int depth, out string text)
    {
        if (_facts.TryLex(name, out var lex))
        {
            var grammar = lex.Culture is null ? _grammar : _data.For(lex.Culture);
            if (grammar.TryWords(lex.Key, out var words)) return TryWord(lex.Key, words, path, depth, out text);
            text = lex.Fallback;
            return text.Length > 0;
        }
        if (_facts.TryValue(name, out text))
        {
            // A name already said in this event, or said in full a few events back, is said in its short form
            // ("the Hold of Varn" ... "Varn").
            if (_facts.TryShort(name, out var shortForm) && (!_mentioned.Add(text) || _memory.ShouldShorten(text))) text = shortForm;
            return true;
        }
        if (_grammar.TryWords(name, out var list)) return TryWord(name, list, path, depth, out text);
        text = string.Empty;
        return false;
    }

    private bool TryWord(string key, string[] words, string path, int depth, out string text)
    {
        string wordPath = NextPath(path, "@" + key);
        var rng = HistoryRng.Create(_seed, Stage, _turn, _eventId, wordPath);
        text = _memory.ChooseWord(key, words, _usedWords, ref rng);
        if (text.IndexOf('{') < 0) return true;
        if (depth > MaxDepth || !ChronicleTemplate.TryParse(text, out var template, out _)) return false;
        var sb = new StringBuilder(text.Length + 32);
        if (!ExpandTemplate(template!, wordPath, depth + 1, sb)) return false;
        text = sb.ToString();
        return true;
    }
}
