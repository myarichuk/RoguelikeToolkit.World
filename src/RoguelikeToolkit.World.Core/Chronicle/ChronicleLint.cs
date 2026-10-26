using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Static checks of chronicle data, per culture grammar (common plus the overlay):
/// <list type="bullet">
/// <item>every event kind has a root symbol, and every symbol a template names exists;</item>
/// <item>every slot is a fact <see cref="ChronicleVocabulary"/> declares or a lexicon list;</item>
/// <item>every predicate tests a declared fact, names only values a closed vocabulary has, and can hold together with the
/// rule's other predicates;</item>
/// <item>every symbol is reachable from a root, so every rule can be drawn;</item>
/// <item>the lexicon has a list for every value of the closed vocabularies a fact renders from (seasons, terrains, peoples...).</item>
/// </list>
/// </summary>
internal static class ChronicleLint
{
    /// <summary>Multi-valued facts: two positive predicates on them may name different values.</summary>
    private static readonly HashSet<string> MultiValued = new(StringComparer.Ordinal) { "beat", "tag", "power" };

    /// <summary>Lexicon lists a fact can render from, for every value of its vocabulary.</summary>
    internal static IEnumerable<string> RequiredLists()
    {
        foreach (var s in ChronicleVocabulary.Closed["season"]) yield return "season." + s;
        foreach (var t in ChronicleVocabulary.Closed["terrain"]) yield return "terrain." + t;
        foreach (var l in ChronicleVocabulary.Closed["att.loss"]) yield return "loss." + l;
        foreach (var t in ChronicleVocabulary.Closed["tier.to"]) yield return "tier." + t;
        foreach (var r in ChronicleVocabulary.Closed["role"]) yield return "role." + r;
        foreach (var a in ChronicleVocabulary.Closed["agenda"]) yield return "agenda." + a;
        foreach (var folk in ChronicleVocabulary.FolkOf.Values)
        {
            yield return "people." + folk;
            yield return "adj." + folk;
        }
        yield return "host";
        yield return "cry";
        for (int k = 0; k <= (int)HistoryEventKind.Prophecy; k++) yield return "what." + (HistoryEventKind)k;
    }

    /// <summary>Every problem in <paramref name="data"/> for the common grammar and each of <paramref name="cultures"/>.</summary>
    public static List<string> Check(ChronicleData data, IEnumerable<string> cultures)
    {
        var problems = new List<string>(data.Problems);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in new[] { ChronicleData.Common }.Concat(cultures))
        {
            var g = data.For(culture);
            foreach (var p in CheckGrammar(g))
                if (seen.Add(p)) problems.Add($"[{culture}] {p}");
        }
        return problems;
    }

    private static IEnumerable<string> CheckGrammar(ChronicleGrammar g)
    {
        var roots = new List<string> { TemplateChronicleTextProvider.GenericRoot };
        for (int k = 0; k <= (int)HistoryEventKind.Prophecy; k++) roots.Add(ChronicleFactBuilder.RootOf((HistoryEventKind)k));
        foreach (var root in roots)
            if (!g.TryRules(root, out var r) || r.Length == 0) yield return $"no rules for root '{root}'.";

        foreach (var list in RequiredLists())
            if (!g.HasWords(list)) yield return $"lexicon has no list '{list}'.";

        // Reachability over symbol references (rules and lexicon words).
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
            string s = queue.Dequeue();
            if (!reached.Add(s) || !g.TryRules(s, out var rules)) continue;
            foreach (var rule in rules)
                foreach (var part in rule.Template.Parts)
                    if (part.Kind == ChronicleTemplate.PartKind.Symbol) queue.Enqueue(part.Text);
        }
        foreach (var key in g.LexiconKeys)
        {
            g.TryWords(key, out var words);
            foreach (var w in words)
            {
                if (!ChronicleTemplate.TryParse(w, out var t, out var err)) { yield return $"lexicon {key}: '{w}': {err}."; continue; }
                foreach (var part in t!.Parts)
                {
                    if (part.Kind == ChronicleTemplate.PartKind.Symbol && !g.TryRules(part.Text, out _))
                        yield return $"lexicon {key}: '{w}' names unknown symbol '{part.Text}'.";
                    if (part.Kind == ChronicleTemplate.PartKind.Slot && !ChronicleVocabulary.IsSlot(part.Text) && !g.HasWords(part.Text))
                        yield return $"lexicon {key}: '{w}' names unknown slot '{part.Text}'.";
                }
            }
        }

        foreach (var symbol in g.Symbols)
        {
            if (!reached.Contains(symbol)) yield return $"symbol '{symbol}' is unreachable from any root.";
            g.TryRules(symbol, out var rules);
            foreach (var rule in rules)
            {
                foreach (var part in rule.Template.Parts)
                {
                    if (part.Kind == ChronicleTemplate.PartKind.Symbol && !g.TryRules(part.Text, out _))
                        yield return $"{rule}: unknown symbol '{part.Text}'.";
                    if (part.Kind == ChronicleTemplate.PartKind.Slot && !ChronicleVocabulary.IsSlot(part.Text) && !g.HasWords(part.Text))
                        yield return $"{rule}: unknown slot '{part.Text}'.";
                }
                foreach (var p in rule.When)
                {
                    if (!ChronicleVocabulary.IsSlot(p.Key)) { yield return $"{rule}: predicate '{p}' tests unknown fact '{p.Key}'."; continue; }
                    if (ChronicleVocabulary.Closed.TryGetValue(p.Key, out var allowed))
                        foreach (var v in p.Values)
                            if (!allowed.Contains(v, StringComparer.OrdinalIgnoreCase))
                                yield return $"{rule}: predicate '{p}' names '{v}', not one of {string.Join("|", allowed)}.";
                }
                string? contradiction = Contradiction(rule);
                if (contradiction != null) yield return $"{rule}: unsatisfiable: {contradiction}.";
            }
        }
    }

    /// <summary>Why the rule's predicates can never hold together, or null.</summary>
    private static string? Contradiction(ChronicleRule rule)
    {
        var must = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var absent = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var p in rule.When)
        {
            if (p.Values.Length == 0)
            {
                (p.Negated ? absent : present).Add(p.Key);
                continue;
            }
            if (p.Negated)
            {
                if (!excluded.TryGetValue(p.Key, out var ex)) excluded[p.Key] = ex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in p.Values) ex.Add(v);
                continue;
            }
            present.Add(p.Key);
            if (MultiValued.Contains(p.Key)) continue;
            var values = new HashSet<string>(p.Values, StringComparer.OrdinalIgnoreCase);
            if (must.TryGetValue(p.Key, out var prior))
            {
                prior.IntersectWith(values);
                if (prior.Count == 0) return $"'{p.Key}' cannot take two values";
            }
            else must[p.Key] = values;
        }
        foreach (var k in present)
            if (absent.Contains(k)) return $"'{k}' both required and excluded";
        foreach (var kv in must)
            if (excluded.TryGetValue(kv.Key, out var ex) && kv.Value.All(ex.Contains)) return $"every value of '{kv.Key}' is excluded";
        return null;
    }
}
