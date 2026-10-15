using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// M1.0 safety net: end-to-end coverage for the operators that default worlds never exercise
/// (Khan, Rebellion) or only exercise implicitly (Treaty, HolySite).
/// </summary>
public class HistoryCoverageTests
{
    private const int Size = 3;

    private static World Build(int seed, Action<HistoryContext>? configure = null)
    {
        var builder = new WorldBuilder().WithSize(Size).WithSeed(seed).WithUserEdits();
        if (configure != null) builder.WithHistory(configure);
        return builder.Build();
    }

    private static void MakeHordes(HistoryCatalog h)
    {
        var old = h.Polities.ToList();
        h.Polities.Clear();
        foreach (var p in old)
        {
            h.Polities.Add(new Polity
            {
                Id = p.Id, Form = PolityForm.Horde, CapitalTile = p.CapitalTile, FolkKey = p.FolkKey,
                CultureKey = p.CultureKey, FoundedTurn = p.FoundedTurn, Population = p.Population,
                Pressure = p.Pressure, CultureOverlay = p.CultureOverlay, RulerGeneration = p.RulerGeneration,
                OpennessDrift = p.OpennessDrift, HeirBornTurn = p.HeirBornTurn, HasVillain = p.HasVillain,
            });
        }
    }

    private static void RunKhan(World w)
        => new HistoryDriver().AddOperator(new PopulationOperator()).AddOperator(new KhanOperator())
            .Apply(w.Map, w.History, new HistoryContext());

    private static bool HasTag(HistoryEvent e, string tag) => e.Tags.Contains(tag);

    [Fact]
    public void Khan_EndToEnd_ProducesTributeOrUltimatum_WhenHordesBorderWeakerNeighbors()
    {
        int tributes = 0, ultimatums = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            MakeHordes(h);
            RunKhan(w);
            foreach (var e in h.Events)
            {
                if (e.Kind == HistoryEventKind.Tribute)
                {
                    tributes++;
                    Assert.Contains(h.Treaties, win => win.UntilTurn >= e.Turn);
                    int to = int.Parse(e.Tags.First(t => t.StartsWith("to:")).Substring(3));
                    int from = int.Parse(e.Tags.First(t => t.StartsWith("from:")).Substring(5));
                    // A later khan event between the pair (reverse direction) may legitimately raise a new grudge.
                    bool reraised = h.Events.Any(u => !ReferenceEquals(u, e) && u.Turn >= e.Turn && u.Kind == HistoryEventKind.Ultimatum
                        && ((u.Tags.Contains($"khan:{to}") && u.Tags.Contains($"weak:{from}"))
                            || (u.Tags.Contains($"khan:{from}") && u.Tags.Contains($"weak:{to}"))));
                    if (!reraised) Assert.Equal(0, h.GrudgeWeight(to, from));
                }
                if (e.Kind == HistoryEventKind.Ultimatum)
                {
                    ultimatums++;
                    int khan = int.Parse(e.Tags.First(t => t.StartsWith("khan:")).Substring(5));
                    int weak = int.Parse(e.Tags.First(t => t.StartsWith("weak:")).Substring(5));
                    // A later tribute between the pair (in either direction) legitimately repays the grudge.
                    bool repaid = h.Events.Any(t => t.Kind == HistoryEventKind.Tribute && t.Turn >= e.Turn
                        && ((t.Tags.Contains($"from:{weak}") && t.Tags.Contains($"to:{khan}"))
                            || (t.Tags.Contains($"from:{khan}") && t.Tags.Contains($"to:{weak}"))));
                    if (!repaid) Assert.True(h.GrudgeWeight(khan, weak) >= KhanOperator.ResistGrudge);
                }
            }
        }
        Assert.True(tributes + ultimatums > 0, "Khan never fired on any forced-horde world.");
    }

    [Fact]
    public void Khan_NeverPressesSamePairTwice()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            MakeHordes(h);
            RunKhan(w);
            var pairs = new List<string>();
            foreach (var e in h.Events)
            {
                if (e.Kind == HistoryEventKind.Ultimatum)
                    pairs.Add($"{TagValue(e, "khan:")}>{TagValue(e, "weak:")}");
                else if (e.Kind == HistoryEventKind.Tribute)
                    pairs.Add($"{TagValue(e, "to:")}>{TagValue(e, "from:")}");
            }
            Assert.Equal(pairs.Count, pairs.Distinct().Count());
        }
    }

    private static string TagValue(HistoryEvent e, string prefix)
        => e.Tags.First(t => t.StartsWith(prefix, StringComparison.Ordinal)).Substring(prefix.Length);

    [Fact]
    public void Treaty_EndToEnd_MarriagesAndFostersOpenWindows_RefusalsRaiseGrudges()
    {
        int marriages = 0, fosters = 0, refusals = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            foreach (var e in h.Events)
            {
                if (e.Kind == HistoryEventKind.Treaty && HasTag(e, "marriage")) marriages++;
                if (e.Kind == HistoryEventKind.Treaty && HasTag(e, "foster")) fosters++;
                if (e.Kind == HistoryEventKind.Grudge && e.Tags.Any(t => t.StartsWith("refused:"))) refusals++;
                if (e.Kind == HistoryEventKind.Treaty)
                {
                    int a = int.Parse(e.Tags.First(t => t.StartsWith("party:")).Substring(6));
                    int b = int.Parse(e.Tags.Last(t => t.StartsWith("party:")).Substring(6));
                    Assert.NotEqual(a, b);
                    Assert.True(h.IsWindowed(a, b, e.Turn) || h.Treaties.Any(win => win.UntilTurn >= e.Turn),
                        "A sealed treaty must leave a non-aggression window.");
                }
            }
            Assert.True(h.Polities.All(p => p.HeirBornTurn >= -1));
        }
        Assert.True(marriages > 0, "No marriage across seeds 1-12.");
        Assert.True(refusals > 0, "No refused treaty across seeds 1-12.");
        Assert.True(fosters > 0, "No foster treaty across seeds 1-12.");
    }

    [Fact]
    public void Rebellion_EndToEnd_SplitsAProvince_WhenThePolityCapAllows()
    {
        int rebellions = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            using var w = Build(seed, c => c.MaxPolities = 16);
            var h = w.History;
            foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.Rebellion))
            {
                rebellions++;
                int parent = int.Parse(TagValue(e, "parent:"));
                var rebel = h.Polities[e.PolityId];
                Assert.NotEqual(parent, e.PolityId);
                Assert.Equal(h.Polities[parent].FolkKey, rebel.FolkKey);
                Assert.Equal(e.Turn, rebel.FoundedTurn);
                Assert.Equal(e.PolityId, h.Ownership[rebel.CapitalTile]);
            }
            Assert.True(h.Polities.Count <= 16);
        }
        Assert.True(rebellions > 0, "No rebellion across seeds 1-20 with MaxPolities=16.");
    }

    /// <summary>
    /// Regenerates <c>docs/history-reference-corpus.md</c> when <c>HISTORY_CORPUS_OUT</c> names the file.
    /// A before/after sanity reference for the M1 port, not a golden: outcomes may shift legitimately.
    /// </summary>
    [Fact]
    public void ReferenceCorpus_CaptureWhenRequested()
    {
        string? path = Environment.GetEnvironmentVariable("HISTORY_CORPUS_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var kinds = Enum.GetValues<HistoryEventKind>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# History reference corpus (pre-M1, stage-major engine)");
        sb.AppendLine();
        sb.AppendLine("Per-world aggregates captured before the M1 port. A sanity reference, not a golden.");
        sb.AppendLine("Regenerate: `HISTORY_CORPUS_OUT=docs/history-reference-corpus.md dotnet test --filter ReferenceCorpus`.");
        sb.AppendLine();
        sb.AppendLine("| size | seed | polities | roads | severed | sites | holy | villains | faiths | grudges | windows | " + string.Join(" | ", kinds) + " |");
        sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", 11 + kinds.Length)));
        var grid = Enumerable.Range(1, 20).Select(s => (Size: 3, Seed: s)).Concat(Enumerable.Range(1, 5).Select(s => (Size: 4, Seed: s)));
        foreach (var (size, seed) in grid)
        {
            using var w = new WorldBuilder().WithSize(size).WithSeed(seed).WithUserEdits().Build();
            var h = w.History;
            sb.AppendLine($"| {size} | {seed} | {h.Polities.Count} | {h.Roads.Count} | {h.Roads.Count(r => r.Severed)} | {h.Sites.Count} | {h.HolySites.Count} | {h.Villains.Count} | {h.Faiths.Count} | {h.Grudges.Count} | {h.Treaties.Count} | "
                + string.Join(" | ", kinds.Select(k => h.Events.Count(e => e.Kind == k))) + " |");
        }
        File.WriteAllText(path, sb.ToString());
    }
}
