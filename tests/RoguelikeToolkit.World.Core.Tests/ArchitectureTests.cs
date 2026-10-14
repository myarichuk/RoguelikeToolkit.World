using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Guards the shape of <c>RoguelikeToolkit.World.Core</c>: namespace == folder, and slices only depend "downwards".
/// <para>
/// Works on source, not IL. After <c>IDE0005</c> pruning, the <c>using RoguelikeToolkit.World.Core.X;</c> lines of a file
/// are exactly the slices it depends on, which makes the dependency graph cheap and exact at namespace granularity
/// (it does not see a fully qualified reference that skips the <c>using</c>; the build has none).
/// </para>
/// <para>
/// The layering is a ratchet: every upward edge that exists today is listed in <see cref="Waivers"/> with a reason.
/// A new upward edge fails the test, and so does a waiver that is no longer needed, so the list only ever shrinks.
/// </para>
/// </summary>
public sealed class ArchitectureTests(ITestOutputHelper output)
{
    private const string Base = "RoguelikeToolkit.World.Core";
    private const string Root = "(root)";

    /// <summary>Layer rank: a slice may depend on slices of equal or lower rank. Equal rank is allowed, cycles inside a rank are only reported.</summary>
    private static readonly Dictionary<string, int> Rank = new()
    {
        ["Primitives"] = 0,
        ["Actors"] = 1, ["Lineage"] = 1, ["Entities"] = 1, ["Localization"] = 1, ["Topology"] = 1, ["Storage"] = 1,
        ["Generation"] = 2, ["Peoples"] = 2,
        ["Tectonics"] = 3, ["Elevation"] = 3, ["Climate"] = 3, ["Hydrology"] = 3, ["Biomes"] = 3, ["Lithosphere"] = 3, ["Settlement"] = 3,
        ["Features"] = 4, ["Hierarchy"] = 4, ["History"] = 4,
        ["Sites"] = 5, ["Integration"] = 5, ["Queries"] = 5, ["Names"] = 5,
        [Root] = 6,
    };

    /// <summary>Known upward edges, as <c>Source-&gt;Target:File</c>. Burn this list down; never add to it without a reason.</summary>
    private static readonly Dictionary<string, string> Waivers = new()
    {
        // The generic pipeline is also the composition root: [DynamicDependency] keeps built-in stages alive under IL trimming/AOT.
        ["Generation->Tectonics:WorldGenerationPipeline"] = "AOT DynamicDependency hooks for built-in stages",
        ["Generation->Elevation:WorldGenerationPipeline"] = "AOT DynamicDependency hooks for built-in stages",
        ["Generation->Climate:WorldGenerationPipeline"] = "AOT DynamicDependency hooks for built-in stages",
        ["Generation->Hydrology:WorldGenerationPipeline"] = "AOT DynamicDependency hooks for built-in stages",
        ["Generation->Biomes:WorldGenerationPipeline"] = "AOT DynamicDependency hooks for built-in stages",
        // The user-edit layer pins biome / climate / land-use values, so Storage sees their enums.
        ["Storage->Biomes:UserEditInfo"] = "UserEditInfo.PinBiome is a BiomeType",
        ["Storage->Climate:UserEditInfo"] = "doc cref to ClimateInfo.Temperature",
        ["Storage->Settlement:UserEditInfo"] = "UserEditInfo.LandUse is a LandUseId",
        ["Settlement->Queries:CitySiteScorer"] = "scorer uses SpatialIndex feature queries",
        // Hydrology produces catalogs that Features reads, and also consumes TileFeatures.
        ["Hydrology->Features:HydrologyStage"] = "river water source / feature types",
        ["Hydrology->Features:HydrologyInfo"] = "river water source / feature types",
        ["Hydrology->Features:RangeCatalogBuilder"] = "river water source / feature types",
        // TileFeatures is the per-hex aggregator: it joins every slice plus placed sites and history, so it sits above them.
        ["Features->Sites:TileFeatures"] = "aggregator joins placed sites",
        ["Features->Sites:TileFeatureInfo"] = "aggregator joins placed sites",
        ["Features->Integration:TileFeatures"] = "aggregator joins history hooks",
        // Region/local handles host the site injectors that run on them.
        ["Hierarchy->Sites:LocalMapHandle"] = "handles run the site injector pipeline",
        ["Hierarchy->Sites:RegionHandle"] = "handles run the site injector pipeline",
        // City-site scoring reads sites, history and feature info.
        ["Settlement->Features:CitySiteScorer"] = "scorer consumes TileFeatures",
        ["Settlement->Integration:CitySiteScorer"] = "scorer consumes IHistoricalContext",
        ["Settlement->Sites:SettlementScoring"] = "scoring is shared with the settlement injector",
        // The catalog is the one history surface: it implements the game-side hook (tile modifiers + historic sites).
        ["History->Integration:HistoryCatalog"] = "HistoryCatalog implements IHistoricalContext",
    };

    [Fact]
    public void Namespace_MatchesFolder()
    {
        var bad = new List<string>();
        foreach (var f in SourceFiles())
        {
            var expected = f.Slice == Root ? Base : $"{Base}.{f.Slice}";
            var m = Regex.Match(f.Text, @"(?m)^namespace ([\w.]+)\s*;");
            if (!m.Success || m.Groups[1].Value != expected)
                bad.Add($"{f.Slice}/{f.Name}: namespace is '{(m.Success ? m.Groups[1].Value : "<none>")}', expected '{expected}'");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void OneTopLevelType_PerFile_NamedAfterTheFile()
    {
        // A cheap guard against the pre-2.0 "SiteInjectors.cs holds 14 types" problem. Nested types are fine.
        var bad = new List<string>();
        var decl = new Regex(@"(?m)^(?:public |internal )?(?:(?:sealed|static|abstract|readonly|unsafe|partial|ref) )*(?:class|struct|enum|interface|record(?: class| struct)?|delegate [\w<>\[\]?,. ]+?) (\w+)");
        foreach (var f in SourceFiles())
        {
            var names = decl.Matches(f.Text).Select(m => m.Groups[1].Value).Distinct().ToList();
            if (names.Count > 1) bad.Add($"{f.Slice}/{f.Name}.cs declares {string.Join(", ", names)}");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Layering_OnlyWaivedUpwardEdges()
    {
        var upward = UpwardEdges();
        var unwaived = upward.Where(e => !Waivers.ContainsKey(e)).ToList();
        var stale = Waivers.Keys.Where(w => !upward.Contains(w)).ToList();

        Assert.True(unwaived.Count == 0, "New upward dependencies (fix them or waive with a reason):" + Environment.NewLine + string.Join(Environment.NewLine, unwaived));
        Assert.True(stale.Count == 0, "Waivers that are no longer needed (delete them):" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void Primitives_DependOnNothing()
    {
        var edges = Edges().Where(e => e.From == "Primitives").ToList();
        Assert.True(edges.Count == 0, string.Join(Environment.NewLine, edges.Select(e => $"{e.From}->{e.To}:{e.File}")));
    }

    /// <summary>Report mode: prints slice cycles. Not asserted yet, because the domain slices read each other's layer structs both ways.</summary>
    [Fact]
    public void Report_SliceCycles()
    {
        var graph = Edges().GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().ToList());
        var sccs = Tarjan(graph).Where(c => c.Count > 1).ToList();
        output.WriteLine($"{sccs.Count} cyclic group(s) among slices.");
        foreach (var c in sccs) output.WriteLine("  {" + string.Join(", ", c.OrderBy(x => x, StringComparer.Ordinal)) + "}");
        foreach (var g in graph.OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"{g.Key} -> {string.Join(", ", g.Value.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    private static HashSet<string> UpwardEdges() => Edges()
        .Where(e => Rank.TryGetValue(e.From, out var rf) && Rank.TryGetValue(e.To, out var rt) && rt > rf)
        .Select(e => $"{e.From}->{e.To}:{e.File}")
        .ToHashSet();

    private sealed record SourceFile(string Slice, string Name, string Text);
    private sealed record Edge(string From, string To, string File);

    private static List<Edge> Edges()
    {
        var uses = new Regex($@"(?m)^using {Regex.Escape(Base)}\.(\w+);");
        var edges = new List<Edge>();
        foreach (var f in SourceFiles())
            foreach (var to in uses.Matches(f.Text).Select(m => m.Groups[1].Value).Distinct())
                if (to != f.Slice) edges.Add(new Edge(f.Slice, to, f.Name));
        return edges;
    }

    /// <summary>"HydrologyStage.Catalogs.cs" is a part of type HydrologyStage: waivers are per type, not per partial file.</summary>
    private static string TypeNameOf(string relPath)
    {
        var name = Path.GetFileNameWithoutExtension(relPath);
        var dot = name.IndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    private static List<SourceFile> SourceFiles([CallerFilePath] string here = "")
    {
        var core = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "RoguelikeToolkit.World.Core"));
        Assert.True(Directory.Exists(core), $"Core sources not found at {core}");
        var list = new List<SourceFile>();
        foreach (var path in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(core, path).Replace('\\', '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.StartsWith("Internal/")) continue;
            var slash = rel.IndexOf('/');
            list.Add(new SourceFile(slash < 0 ? Root : rel[..slash], TypeNameOf(rel), File.ReadAllText(path)));
        }
        return list;
    }

    private static List<List<string>> Tarjan(Dictionary<string, List<string>> g)
    {
        var index = new Dictionary<string, int>(); var low = new Dictionary<string, int>();
        var stack = new Stack<string>(); var on = new HashSet<string>(); var result = new List<List<string>>(); int counter = 0;
        void Visit(string v)
        {
            index[v] = low[v] = counter++; stack.Push(v); on.Add(v);
            foreach (var w in g.GetValueOrDefault(v) ?? [])
            {
                if (!index.ContainsKey(w)) { Visit(w); low[v] = Math.Min(low[v], low[w]); }
                else if (on.Contains(w)) low[v] = Math.Min(low[v], index[w]);
            }
            if (low[v] != index[v]) return;
            var comp = new List<string>(); string x;
            do { x = stack.Pop(); on.Remove(x); comp.Add(x); } while (x != v);
            result.Add(comp);
        }
        foreach (var v in g.Keys.ToList()) if (!index.ContainsKey(v)) Visit(v);
        return result;
    }
}
