using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M5.1: subsistence profiles, resources, the era gate and the folk allow-list.</summary>
public sealed class PeoplesResourceTests
{
    private static PeoplesStore LoadWith(Action<string> write, out IReadOnlyList<string> problems)
    {
        string dir = Path.Combine(Path.GetTempPath(), "peoples-m51-" + Guid.NewGuid().ToString("N"));
        foreach (var sub in new[] { "Folks", "Cultures", "Subsistence", "Resources" }) Directory.CreateDirectory(Path.Combine(dir, sub));
        try
        {
            write(dir);
            return PeoplesStore.LoadLenient(dir, out problems);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ShippedData_Loads_AndClaimsEveryDepositTypeOnce()
    {
        var store = PeoplesStore.Load();
        foreach (var key in new[] { "agrarian", "pastoral", "artisan", "maritime", "forager", "highland" })
            Assert.Equal(key, store.GetSubsistence(key).Key);
        Assert.Empty(ResourceDeposits.Problems(store));
        var claimed = new List<DepositType>();
        foreach (var r in store.Resources.Values)
            if (ResourceDeposits.TryGetDeposit(r, out var d)) claimed.Add(d);
        Assert.Equal(Enum.GetValues<DepositType>().OrderBy(d => d), claimed.OrderBy(d => d));
        foreach (var surface in new[] { "quarry", "lumber", "fishery", "range" })
            Assert.False(ResourceDeposits.TryGetDeposit(store.GetResource(surface), out _));
        foreach (var c in store.Cultures.Values) Assert.True(store.Subsistence.ContainsKey(c.Subsistence), c.Key);
    }

    [Fact]
    public void ShippedFantasyEra_KeepsIndustrialResourcesOut()
    {
        var store = PeoplesStore.Load();
        foreach (var key in new[] { "uranium", "oil", "gas", "bauxite" })
            Assert.Equal(ResourceEra.Industrial, store.GetResource(key).EraLevel);
        Assert.Equal(ResourceEra.Medieval, store.GetResource("coal").EraLevel);

        var medieval = store.ExploitsOf("artisan", ResourceEra.Medieval).Select(e => e.Resource).ToList();
        Assert.Contains("coal", medieval);
        Assert.DoesNotContain("uranium", medieval);
        Assert.DoesNotContain("bauxite", medieval);
        Assert.Contains("uranium", store.ExploitsOf("artisan", ResourceEra.Industrial).Select(e => e.Resource));
        Assert.DoesNotContain("coal", store.ExploitsOf("artisan", ResourceEra.Ancient).Select(e => e.Resource));
    }

    [Fact]
    public void Pastoralists_DoNotMine()
    {
        var store = PeoplesStore.Load();
        var worked = store.ExploitsOf("pastoral", ResourceEra.Industrial).Select(e => store.GetResource(e.Resource));
        Assert.DoesNotContain(worked, r => ResourceDeposits.TryGetDeposit(r, out var d) && d != DepositType.Salt);
    }

    [Fact]
    public void HistoryContext_DefaultsToMedieval_AndRejectsAnUndefinedEra()
    {
        var ctx = new HistoryContext();
        Assert.Equal(ResourceEra.Medieval, ctx.MaxResourceEra);
        ctx.MaxResourceEra = ResourceEra.Industrial;
        Assert.Equal(ResourceEra.Industrial, ctx.MaxResourceEra);
        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.MaxResourceEra = (ResourceEra)7);
    }

    [Fact]
    public void FolkAllowList_GatesTheCulturePairing()
    {
        var store = PeoplesStore.Load();
        Assert.Equal(new[] { "artisan", "highland" }, store.GetFolk("dwarf").Subsistence);
        Assert.Empty(store.GetFolk("human").Subsistence);
        foreach (var p in new HistoryContext().Peoples) Assert.Null(store.PairingProblem(p.FolkKey, p.CultureKey));
        Assert.True(store.AllowsSubsistence("human", "pastoral"));
        Assert.False(store.AllowsSubsistence("dwarf", "maritime"));
        Assert.Contains("does not allow", store.PairingProblem("dwarf", "human_common"));
        Assert.Contains("unknown culture", store.PairingProblem("dwarf", "nope"));
    }

    [Fact]
    public void StrictLookups_NameTheMissingKey()
    {
        var store = PeoplesStore.Load();
        Assert.Throws<UnknownPeoplesKeyException>(() => store.GetSubsistence("industrialist"));
        Assert.Throws<UnknownPeoplesKeyException>(() => store.GetResource("mithril"));
        Assert.Throws<UnknownPeoplesKeyException>(() => store.ExploitsOf("industrialist", ResourceEra.Medieval));
    }

    [Fact]
    public void BadResources_AreReported()
    {
        LoadWith(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "Resources", "steam.yaml"), "Key: steam\nEra: Steam\nValue: 1\n");
            File.WriteAllText(Path.Combine(dir, "Resources", "peat.yaml"), "Key: peat\nYield: timber\nEra: Ancient\nLifetimeTurns: 20\n");
            File.WriteAllText(Path.Combine(dir, "Resources", "both.yaml"), "Key: both\nDeposit: Iron\nYield: fish\nEra: Ancient\n");
            File.WriteAllText(Path.Combine(dir, "Resources", "bog_iron.yaml"), "Key: bog_iron\nDeposit: Iron\nEra: Ancient\nValue: 3\n");
            File.WriteAllText(Path.Combine(dir, "Resources", "rich.yaml"), "Key: rich\nEra: Ancient\nValue: 500\n");
        }, out var problems);
        Assert.Contains(problems, p => p.Contains("unknown Era 'Steam'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("LifetimeTurns needs a Deposit", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("not both", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("deposit Iron is already claimed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Value 500 is out of range", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownDepositName_IsReportedByTheHistoryBinding()
    {
        var store = LoadWith(dir =>
            File.WriteAllText(Path.Combine(dir, "Resources", "mithril.yaml"), "Key: mithril\nDeposit: Mithril\nEra: Ancient\nValue: 10\n"),
            out var problems);
        Assert.Empty(problems);
        Assert.False(ResourceDeposits.TryGetDeposit(store.GetResource("mithril"), out _));
        Assert.Contains(ResourceDeposits.Problems(store), p => p.Contains("unknown Deposit 'Mithril'", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenReferences_AreReported_AndStrictLoadThrows()
    {
        Action<string> write = dir =>
        {
            File.WriteAllText(Path.Combine(dir, "Subsistence", "raider.yaml"),
                "Key: raider\nSedentary: false\nExploits:\n  - { Resource: plunder, Value: 2 }\n  - { Resource: salt, Value: 1 }\n");
            File.WriteAllText(Path.Combine(dir, "Subsistence", "salter.yaml"),
                "Key: salter\nExploits:\n  - { Resource: salt, Value: 1 }\n  - { Resource: salt, Value: 1 }\n");
            File.WriteAllText(Path.Combine(dir, "Folks", "orc.yaml"),
                "Key: orc\nSapience: Full\nEmbodiment: humanoid\nHabitatAffinity: hills\nLifespanYears: 50\nMaturationYears: 12\nGrowthR: 0.06\n" +
                "CompatGroup: mammal\nOpennessPrior: low\nConscientiousnessPrior: low\nExtraversionPrior: high\nAgreeablenessPrior: low\nNeuroticismPrior: balanced\n" +
                "Subsistence:\n  - raider\n  - reaver\n");
            File.WriteAllText(Path.Combine(dir, "Cultures", "orc_horde.yaml"),
                "Key: orc_horde\nNamingTable: orcish\nSubsistence: nomadic\nLineageDefault: Patrilineal\nFaithForm: shamanism\nMixedNaming: Dominant\n");
        };
        var store = LoadWith(write, out var problems);
        Assert.Contains(problems, p => p.Contains("Subsistence/raider.yaml: unknown resource 'plunder'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("repeats resource 'salt'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Folks/orc.yaml: unknown Subsistence 'reaver'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Cultures/orc_horde.yaml: unknown Subsistence 'nomadic'", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("'raider'", StringComparison.Ordinal));
        Assert.True(store.Folks.ContainsKey("orc"));

        string dir = Path.Combine(Path.GetTempPath(), "peoples-m51s-" + Guid.NewGuid().ToString("N"));
        foreach (var sub in new[] { "Folks", "Cultures", "Subsistence" }) Directory.CreateDirectory(Path.Combine(dir, sub));
        try
        {
            write(dir);
            Assert.Throws<PeoplesDataException>(() => PeoplesStore.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Override_ReplacesAResourceByKey()
    {
        var store = LoadWith(dir =>
            File.WriteAllText(Path.Combine(dir, "Resources", "coal.yaml"), "Key: coal\nDeposit: Coal\nEra: Ancient\nValue: 4\nLifetimeTurns: 40\n"),
            out var problems);
        Assert.Empty(problems);
        var coal = store.GetResource("coal");
        Assert.Equal(ResourceEra.Ancient, coal.EraLevel);
        Assert.Equal(40, coal.LifetimeTurns);
        Assert.Contains("coal", store.ExploitsOf("agrarian", ResourceEra.Ancient).Select(e => e.Resource));
    }
}
