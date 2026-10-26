using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RoguelikeToolkit.World.Core.Chronicle;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X7 (docs/HISTORY_EXPANSION_PLAN.md §6, §11 "Chronicle"): the late renderer. The shipped grammar lints clean for every
/// culture; every rule renders through every overlay; every event kind renders for every culture; text over a corpus of
/// generated worlds stays varied (most frequent sentence under 2%, trigram repeats under 15% in any 20-event window); the
/// text is deterministic and rendering never changes the catalog.
/// </summary>
public class ChronicleTests
{
    private readonly ITestOutputHelper _output;

    public ChronicleTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed, int size = 3, int turns = 30, Action<HistoryContext>? peoples = null)
        => new WorldBuilder().WithSize(size).WithSeed(seed).WithHistory(c =>
        {
            c.Features = HistoryFeatures.All;
            c.TurnCount = turns;
            peoples?.Invoke(c);
        }).Build();

    private static void AssertClean(string text, string context)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), $"empty text for {context}");
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("}", text);
        Assert.DoesNotContain("  ", text);
    }

    // ---------------------------------------------------------------- data and lint

    [Fact]
    public void ShippedData_LoadsClean_WithAnOverlayForEveryCulture()
    {
        var data = ChronicleData.Shared;
        Assert.Empty(data.Problems);
        var shipped = PeoplesStore.Load().Cultures.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(12, shipped.Count);
        Assert.Equal(shipped, ChronicleVocabulary.Cultures);
        Assert.Equal(shipped, data.Cultures);
        foreach (var culture in shipped)
            Assert.Equal(ChronicleVocabulary.FolkOf[culture], ChronicleVocabulary.FolkOf[culture]);
    }

    [Fact]
    public void Lint_ShippedGrammar_IsClean_ForEveryCulture()
    {
        var problems = ChronicleLint.Check(ChronicleData.Shared, ChronicleVocabulary.Cultures);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Lint_CatchesBrokenData()
    {
        var data = ChronicleData.FromSources(new (string, string?)[]
        {
            ("Grammar/common.yaml", """
                event.generic:
                  - { id: a, t: "{#missing} {nofact} ok" }
                  - { id: b, when: [season=wintr], t: "x" }
                  - { id: c, when: [winner=attacker, winner=defender], t: "y" }
                  - { id: d, when: [realm, "!realm"], t: "z" }
                  - { id: e, when: [bogus_key=1], t: "w" }
                  - { id: f, t: "{unclosed" }
                  - { id: a, t: "dup" }
                orphan:
                  - { id: o1, t: "never reached" }
                """),
            ("Lexicon/common.yaml", "host: [army]\n"),
        });
        var all = string.Join("\n", ChronicleLint.Check(data, Array.Empty<string>()));
        _output.WriteLine(all);
        Assert.Contains("unknown symbol 'missing'", all);
        Assert.Contains("unknown slot 'nofact'", all);
        Assert.Contains("names 'wintr'", all);
        Assert.Contains("'winner' cannot take two values", all);
        Assert.Contains("'realm' both required and excluded", all);
        Assert.Contains("unknown fact 'bogus_key'", all);
        Assert.Contains("unclosed", all);
        Assert.Contains("duplicate id", all);
        Assert.Contains("'orphan' is unreachable", all);
        Assert.Contains("no rules for root 'event.War'", all);
        Assert.Contains("lexicon has no list 'season.winter'", all);
    }

    [Fact]
    public void Lexicon_CoversTheShippedVocabularies()
    {
        var peoples = PeoplesStore.Load();
        var g = ChronicleData.Shared.For(ChronicleData.Common);
        var missing = new List<string>();
        foreach (var tactic in peoples.Tactics.Keys) if (!g.HasWords("tactic." + tactic)) missing.Add("tactic." + tactic);
        foreach (var e in peoples.TraitTables["epithets"].Epithets) if (!g.HasWords("epithet." + e.Key)) missing.Add("epithet." + e.Key);
        foreach (var form in peoples.Courts["default"].Forms.Values)
            foreach (var office in form)
                if (!g.HasWords("office." + office.Office)) missing.Add("office." + office.Office);
        foreach (var culture in peoples.Cultures.Values)
            if (!g.HasWords("faith." + culture.FaithForm)) missing.Add("faith." + culture.FaithForm);
        Assert.True(missing.Count == 0, string.Join(", ", missing));

        // Every beat the battle data can record is told by some rule (a turning point, a fate, or a siege's course or end).
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in g.Symbols)
        {
            g.TryRules(symbol, out var rules);
            foreach (var r in rules)
                foreach (var p in r.When)
                    if (p.Key == "beat" && !p.Negated) foreach (var v in p.Values) named.Add(v);
        }
        var untold = peoples.Battles["default"].Beats.Select(b => b.Key).Where(k => !named.Contains(k) && k != "winter_quarters").ToList();
        Assert.True(untold.Count == 0, "beats no rule tells: " + string.Join(", ", untold));
    }

    // ---------------------------------------------------------------- every rule, every culture

    [Fact]
    public void EveryRule_RendersThroughEveryCulture_WithFullFacts()
    {
        var data = ChronicleData.Shared;
        int rendered = 0;
        foreach (var culture in new[] { ChronicleData.Common }.Concat(ChronicleVocabulary.Cultures))
        {
            var g = data.For(culture);
            var expander = new ChronicleExpander(data, new ChronicleMemory()) { IgnorePredicates = true };
            var facts = FullFacts(culture);
            foreach (var symbol in g.Symbols)
            {
                g.TryRules(symbol, out var rules);
                foreach (var rule in rules)
                {
                    string text = expander.Render(symbol, rule, facts, 5, 3, rendered);
                    if (rule.Template.Parts.Length > 0) AssertClean(text, $"{culture}: {rule}");
                    rendered++;
                }
            }
        }
        _output.WriteLine($"{rendered} rule renders across 13 grammars");
    }

    /// <summary>Every declared slot set, lexicon-backed ones pointing at a list that exists.</summary>
    private static ChronicleFacts FullFacts(string culture)
    {
        var f = new ChronicleFacts { Culture = culture };
        foreach (var slot in ChronicleVocabulary.Slots) f.Set(slot, "X" + slot.Replace(".", ""));
        f.SetLex("season.word", "season.winter", "winter");
        f.SetLex("terrain.noun", "terrain.forest", "forest");
        f.SetLex("what", "what.War", "war");
        f.SetLex("settlement.kind", "tier.Town", "town");
        foreach (var p in ChronicleVocabulary.RealmPrefixes)
        {
            f.SetLex(p + "people", "people.dwarf", "dwarves");
            f.SetLex(p + "adj", "adj.dwarf", "dwarven");
            f.SetLex(p + "host", "host", "host", culture);
            f.SetLex(p + "cry", "cry", "Onward!", culture);
        }
        foreach (var p in ChronicleVocabulary.PersonPrefixes)
        {
            f.SetLex(p + ".epithet", "epithet.bold", "the Bold");
            f.SetLex(p + ".office", "office.marshal", "marshal");
        }
        return f;
    }

    // ---------------------------------------------------------------- every kind, every culture

    [Fact]
    public void SyntheticEvents_OfEveryKind_RenderForEveryCulture()
    {
        var catalog = ChronicleSynthetic.Build();
        var provider = new TemplateChronicleTextProvider();
        var drawn = new List<ChronicleRule[]>();
        var texts = provider.RenderLog(catalog, drawn);
        var kindsByCulture = new HashSet<(string, HistoryEventKind)>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < texts.Length; i++)
        {
            var e = catalog.Events[i];
            AssertClean(texts[i], $"{e.Kind} #{e.Id} [{string.Join(",", e.Tags)}]");
            Assert.Contains(drawn[i], r => r.Symbol == ChronicleFactBuilder.RootOf(e.Kind));
            if (e.PolityId >= 0) kindsByCulture.Add((catalog.Polities[e.PolityId].CultureKey, e.Kind));
            foreach (var r in drawn[i]) used.Add(r.Origin + ":" + r.Symbol + "/" + r.Id);
        }
        foreach (var culture in ChronicleVocabulary.Cultures)
            for (int k = 0; k <= (int)HistoryEventKind.Prophecy; k++)
                if ((HistoryEventKind)k != HistoryEventKind.Fork)
                    Assert.Contains((culture, (HistoryEventKind)k), kindsByCulture);

        // Coverage: the share of all rules (common and overlays) the synthetic log drew at least once.
        int total = 0, hit = 0;
        var unused = new List<string>();
        var data = ChronicleData.Shared;
        foreach (var culture in new[] { ChronicleData.Common }.Concat(ChronicleVocabulary.Cultures))
        {
            var g = data.For(culture);
            foreach (var symbol in g.Symbols)
            {
                g.TryRules(symbol, out var rules);
                foreach (var r in rules)
                {
                    // event.generic only renders when a kind's own root cannot; the full-facts test renders every rule of it.
                    if (r.Origin != culture || symbol == TemplateChronicleTextProvider.GenericRoot) continue;
                    total++;
                    if (used.Contains(r.Origin + ":" + r.Symbol + "/" + r.Id)) hit++;
                    else unused.Add($"{r.Origin}:{r}");
                }
            }
        }
        _output.WriteLine($"{texts.Length} events, rule coverage {hit}/{total} ({100.0 * hit / total:0.0}%)");
        _output.WriteLine("never drawn: " + string.Join(", ", unused));
        Assert.True(hit >= total * 0.9, $"only {hit}/{total} rules drawn");
    }

    // ---------------------------------------------------------------- determinism and purity

    [Fact]
    public void Rendering_IsDeterministic_AndNeverChangesTheCatalog()
    {
        using var world = Build(3);
        var h = world.History;
        string before = Convert.ToHexString(SHA256.HashData(h.ToBytes()));
        var a = new TemplateChronicleTextProvider().RenderAll(h);
        var b = new TemplateChronicleTextProvider().RenderAll(h);
        string after = Convert.ToHexString(SHA256.HashData(h.ToBytes()));
        Assert.Equal(before, after);
        Assert.Equal(a, b);
        Assert.Equal(h.Events.Count, a.Count);

        using var again = Build(3);
        Assert.Equal(a, new TemplateChronicleTextProvider().RenderAll(again.History));

        var synthetic = ChronicleSynthetic.Build();
        string sb = Convert.ToHexString(SHA256.HashData(synthetic.ToBytes()));
        new TemplateChronicleTextProvider().RenderAll(synthetic);
        Assert.Equal(sb, Convert.ToHexString(SHA256.HashData(synthetic.ToBytes())));
    }

    [Fact]
    public void Render_ById_AndSlices_AgreeWithTheWholeLog()
    {
        using var world = Build(2);
        var h = world.History;
        var provider = new TemplateChronicleTextProvider();
        var all = provider.RenderAll(h);
        for (int i = 0; i < h.Events.Count; i += 7) Assert.Equal(all[i], provider.Render(h, h.Events[i].Id));
        Assert.Equal(all.Skip(10).Take(25), provider.RenderAll(h, 10, 25));
        Assert.Equal(all.Skip(h.Events.Count - 3), provider.RenderAll(h, h.Events.Count - 3));
        Assert.Empty(provider.RenderAll(h, h.Events.Count + 5, 3));
        Assert.Equal(string.Empty, provider.Render(h, -42));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.RenderAll(h, -1));
    }

    [Fact]
    public void Names_HostResolverAndRenames_AreHonoured()
    {
        using var world = Build(4);
        var h = world.History;
        var host = new FixedNames();
        var texts = new TemplateChronicleTextProvider(new ChronicleOptions { Names = host }).RenderAll(h);
        Assert.Contains(texts, t => t.Contains("Realm0", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("Person", StringComparison.Ordinal));

        var provider = new TemplateChronicleTextProvider();
        var before = provider.RenderAll(h);
        int founder = h.Events.First(e => e.Kind == HistoryEventKind.Founding).PolityId;
        h.Overrides.RenamePolity(founder, "Glimmerdeep");
        var renamed = provider.RenderAll(h);
        Assert.Contains(renamed, t => t.Contains("Glimmerdeep", StringComparison.Ordinal));
        Assert.DoesNotContain(before, t => t.Contains("Glimmerdeep", StringComparison.Ordinal));
        h.Overrides.ClearRename(founder);
        Assert.Equal(before, provider.RenderAll(h));
    }

    [Fact]
    public void Intrigue_ReadsDifferently_WhenSecret_Exposed_OrCaught()
    {
        var catalog = ChronicleSynthetic.Build();
        var texts = new TemplateChronicleTextProvider(new ChronicleOptions { Names = new FixedNames() }).RenderAll(catalog);
        int caught = 0;
        for (int i = 0; i < texts.Count; i++)
        {
            var e = catalog.Events[i];
            if (e.Kind != HistoryEventKind.Intrigue) continue;
            int actor = ChronicleFactBuilder.TagInt(e, "actor:");
            if (e.Tags.Contains("caught"))
            {
                // A caught agent names the sponsor.
                caught++;
                Assert.Contains("Realm" + actor, texts[i]);
                Assert.Matches("execut|hang|axe|rack|stopped", texts[i]);
            }
            else if (e.Tags.Contains("plot:sponsorship_exposed")) Assert.Contains("Realm" + actor, texts[i]);
        }
        Assert.True(caught > 0);
    }

    [Fact]
    public void Dates_FollowTheOptions()
    {
        var catalog = ChronicleSynthetic.Build(rounds: 1);
        var e = catalog.Events.First(x => x.Kind == HistoryEventKind.Founding);
        var text = new TemplateChronicleTextProvider(new ChronicleOptions { TurnYears = 25, FirstYear = 1000 }).Render(catalog, e.Id);
        Assert.Contains((1000 + e.Turn * 25).ToString(System.Globalization.CultureInfo.InvariantCulture), text);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChronicleOptions { TurnYears = 0 });
    }

    [Fact]
    public void FactBuilder_WritesOnlyDeclaredFacts()
    {
        var catalog = ChronicleSynthetic.Build(rounds: 1);
        var builder = new ChronicleFactBuilder(catalog, new DefaultChronicleNames(), new ChronicleOptions());
        var undeclared = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in catalog.Events)
            foreach (var key in builder.Build(e).Keys)
                if (!ChronicleVocabulary.IsSlot(key)) undeclared.Add(key);
        Assert.True(undeclared.Count == 0, string.Join(", ", undeclared));
    }

    // ---------------------------------------------------------------- variety over a corpus

    [Fact]
    public void Corpus_TextStaysVaried()
    {
        var logs = new List<IReadOnlyList<string>>();
        var plain = new List<IReadOnlyList<string>>();
        Action<HistoryContext> mixed = c =>
        {
            c.Peoples.Clear();
            c.Peoples.Add(new HistoryPeople { FolkKey = "orc", CultureKey = "orcish_horde" });
            c.Peoples.Add(new HistoryPeople { FolkKey = "elf_wood", CultureKey = "elven_wood" });
            c.Peoples.Add(new HistoryPeople { FolkKey = "halfling", CultureKey = "halfling_hearth" });
        };
        var seeds = new (int Seed, Action<HistoryContext>? Peoples)[] { (1, null), (2, null), (3, null), (4, null), (5, null), (6, null), (7, mixed), (8, mixed) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long renderMs = 0;
        foreach (var (seed, peoples) in seeds)
        {
            using var world = Build(seed, peoples: peoples);
            var t0 = sw.ElapsedMilliseconds;
            logs.Add(new TemplateChronicleTextProvider().RenderAll(world.History));
            if (seed == 1)
                foreach (var g in world.History.Events.Select((e, i) => (e.Kind, Text: logs[^1][i])).GroupBy(x => x.Kind))
                    foreach (var x in g.Take(2)) _output.WriteLine($"[{x.Kind}] {x.Text}");
            renderMs += sw.ElapsedMilliseconds - t0;
            plain.Add(new TemplateChronicleTextProvider(new ChronicleOptions { Names = new FixedNames() }).RenderAll(world.History));
        }

        var (topShare, topSentence, sentences) = TopSentence(logs);
        var (maxWindow, meanWindow) = TrigramRepeats(logs, 20);
        var (plainMax, plainMean) = TrigramRepeats(plain, 20);
        var (plainTop, plainSentence, _) = TopSentence(plain);
        _output.WriteLine($"{logs.Sum(l => l.Count)} events, {sentences} sentences, render {renderMs} ms in all");
        _output.WriteLine($"most frequent sentence {topShare:P2}: \"{topSentence}\"");
        _output.WriteLine($"trigram repeats in 20-event windows: max {maxWindow:P1}, mean {meanWindow:P1}");
        _output.WriteLine($"one-word names: top sentence {plainTop:P2} (\"{plainSentence}\"), trigram max {plainMax:P1}, mean {plainMean:P1}");
        Assert.True(topShare < 0.02, $"top sentence {topShare:P2}: {topSentence}");
        Assert.True(maxWindow < 0.15, $"trigram repeats {maxWindow:P1}");
        foreach (var log in logs)
            foreach (var text in log)
                AssertClean(text, "corpus");
    }

    private static (double Share, string Sentence, int Total) TopSentence(IEnumerable<IReadOnlyList<string>> logs)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int total = 0;
        foreach (var log in logs)
            foreach (var text in log)
                foreach (var s in ChronicleText.Sentences(text))
                {
                    counts[s] = counts.TryGetValue(s, out int n) ? n + 1 : 1;
                    total++;
                }
        var top = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        return ((double)top.Value / total, top.Key, total);
    }

    /// <summary>Per window of <paramref name="window"/> consecutive events: (word trigrams − distinct ones) / trigrams.</summary>
    private static (double Max, double Mean) TrigramRepeats(IEnumerable<IReadOnlyList<string>> logs, int window)
    {
        var word = new Regex("[A-Za-z0-9'-]+");
        double max = 0, sum = 0;
        int windows = 0;
        foreach (var log in logs)
        {
            var grams = log.Select(t =>
            {
                var w = word.Matches(t.ToLowerInvariant()).Select(m => m.Value).ToList();
                var list = new List<string>();
                for (int i = 0; i + 2 < w.Count; i++) list.Add(w[i] + " " + w[i + 1] + " " + w[i + 2]);
                return list;
            }).ToList();
            for (int start = 0; start + window <= grams.Count; start += window / 2)
            {
                var all = grams.Skip(start).Take(window).SelectMany(g => g).ToList();
                if (all.Count == 0) continue;
                double r = (double)(all.Count - all.Distinct(StringComparer.Ordinal).Count()) / all.Count;
                max = Math.Max(max, r);
                sum += r;
                windows++;
            }
        }
        return (max, windows > 0 ? sum / windows : 0);
    }

    // ---------------------------------------------------------------- text helpers

    [Theory]
    [InlineData("dwarf", "pl", "dwarves")]
    [InlineData("war chief", "pl", "war chiefs")]
    [InlineData("city", "pl", "cities")]
    [InlineData("march", "pl", "marches")]
    [InlineData("kinsman", "pl", "kinsmen")]
    [InlineData("elder", "a", "an elder")]
    [InlineData("unicorn", "a", "a unicorn")]
    [InlineData("hour", "a", "an hour")]
    [InlineData("Varn", "poss", "Varn's")]
    [InlineData("the Free Clans", "poss", "the Free Clans'")]
    [InlineData("3", "ord", "third")]
    [InlineData("22", "ord", "22nd")]
    [InlineData("113", "ord", "113th")]
    [InlineData("7", "num", "seven")]
    [InlineData("the Hold of Varn", "the", "Hold of Varn")]
    [InlineData("the hold", "cap", "The hold")]
    public void Modifiers(string input, string modifier, string expected)
        => Assert.Equal(expected, ChronicleText.Apply(input, modifier));

    [Fact]
    public void Tidy_FixesSpacingAndSentenceCase()
    {
        Assert.Equal("The hold fell. Then the rain came.", ChronicleText.Tidy("the hold fell .  then the rain came."));
        Assert.Equal("They roared \"Onward!\" and charged.", ChronicleText.Tidy("they roared \"Onward!\" and charged."));
        Assert.Equal(new[] { "A b.", "C d!", "E?" }, ChronicleText.Sentences("A b. C d! E?").ToArray());
    }

    [Fact]
    public void Predicates_ParseAndMatch()
    {
        var f = new ChronicleFacts();
        f.Set("season", "winter");
        f.Add("beat", "flank");
        f.Add("beat", "rout");
        Holds(f, "season=winter", true);
        Holds(f, "season=Winter|summer", true);
        Holds(f, "season!=winter", false);
        Holds(f, "!season=winter", false);
        Holds(f, "beat=rout", true);
        Holds(f, "beat", true);
        Holds(f, "!terrain", true);
        Holds(f, "terrain", false);
        Assert.False(ChroniclePredicate.TryParse("=x", out _, out _));
        Assert.False(ChroniclePredicate.TryParse("a=", out _, out _));
        Assert.False(ChronicleTemplate.TryParse("{a|shout}", out _, out _));
        Assert.False(ChronicleTemplate.TryParse("{a?}", out _, out _));
        Assert.True(ChronicleTemplate.TryParse("{#a?} {b|cap|poss}", out var t, out _));
        Assert.Equal(3, t!.Parts.Length);
    }

    private static void Holds(ChronicleFacts f, string predicate, bool expected)
    {
        Assert.True(ChroniclePredicate.TryParse(predicate, out var p, out var err), err);
        Assert.Equal(expected, p!.Holds(f));
    }

    [Fact]
    public void Memory_CoolsRecentRules_AndFallsBackToTheLeastRecent()
    {
        ChronicleTemplate.TryParse("x", out var t, out _);
        var rules = Enumerable.Range(0, 4).Select(i => new ChronicleRule("s", "r" + i, Array.Empty<ChroniclePredicate>(), 1, -1, t!, "common")).ToList();
        var memory = new ChronicleMemory();
        var seen = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            var rng = Primitives.Rng.Create(9, i);
            int pick = memory.Choose(rules, ref rng);
            memory.Use(rules[pick]);
            memory.Commit();
            seen.Add(rules[pick].Id);
        }
        // Half the candidates cool: a rule never repeats within two draws.
        for (int i = 2; i < seen.Count; i++) Assert.NotEqual(seen[i], seen[i - 1]);
        for (int i = 2; i < seen.Count; i++) Assert.NotEqual(seen[i], seen[i - 2]);

        var hot = rules.Select(r => new ChronicleRule("h", r.Id, r.When, 1, 10, t!, "common")).ToList();
        var m2 = new ChronicleMemory();
        var order = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            var rng = Primitives.Rng.Create(3, i);
            int pick = m2.Choose(hot, ref rng);
            m2.Use(hot[pick]);
            m2.Commit();
            order.Add(hot[pick].Id);
        }
        // Every rule cools for 10 draws: after the first four the least recently used comes round in turn.
        Assert.Equal(order.Take(4), order.Skip(4).Take(4));
    }

    private sealed class FixedNames : IChronicleNameResolver
    {
        public string PolityName(HistoryCatalog catalog, int polityId)
            => catalog.Overrides.Renames.TryGetValue(polityId, out var n) ? n : "Realm" + polityId;
        public string PersonName(HistoryCatalog catalog, int personId) => "Person" + personId;
        public string SettlementName(HistoryCatalog catalog, int settlementIndex) => "Town" + settlementIndex;
    }
}
