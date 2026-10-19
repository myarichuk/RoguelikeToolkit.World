using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Races as founding peoples: shipped folk and culture data, form affinity, and author-pinned founders.</summary>
public class HistoryFoundersTests
{
    private static readonly string[] Races = { "elf_high", "elf_wood", "elf_dark", "orc", "halfling", "gnome", "lizardfolk", "dragonfolk", "catfolk", "dogfolk" };
    private static readonly string[] Cultures = { "elven_high", "elven_wood", "elven_dark", "orcish_horde", "halfling_hearth", "gnomish_burrow", "lizardfolk_marsh", "dragonfolk_aerie", "catfolk_prowl", "dogfolk_pack" };

    private static World Build(int seed, Action<HistoryContext> configure)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory(configure).Build();

    [Fact]
    public void ShippedRaces_LoadAndEachCulturePairsWithItsFolk()
    {
        var store = PeoplesStore.Load();
        for (int i = 0; i < Races.Length; i++)
        {
            Assert.True(store.Folks.ContainsKey(Races[i]), Races[i]);
            Assert.True(store.Cultures.ContainsKey(Cultures[i]), Cultures[i]);
            Assert.Null(store.PairingProblem(Races[i], Cultures[i]));
            Assert.NotNull(FormAffinity.WeightsOf(store.Cultures[Cultures[i]]));
        }
        Assert.Empty(FormAffinity.Problems(store));
    }

    [Fact]
    public void Affinity_NeverPicksAFormWithZeroWeight_AndFollowsTheWeights()
    {
        var weights = new[] { 0, 30, 70, 0 };   // Kingdom, MerchantRepublic, LooseAssociation, Horde
        var counts = new int[4];
        for (int i = 0; i < 2000; i++) counts[(int)FormAffinity.Pick(weights, PolityForm.Kingdom, (i + 0.5) / 2000)]++;
        Assert.Equal(0, counts[(int)PolityForm.Kingdom] + counts[(int)PolityForm.Horde]);
        Assert.True(counts[(int)PolityForm.LooseAssociation] > counts[(int)PolityForm.MerchantRepublic]);
    }

    [Fact]
    public void Affinity_LeansTowardTheFormTheLandFavours()
    {
        var weights = new[] { 25, 25, 25, 25 };
        int kingdoms = 0;
        for (int i = 0; i < 1000; i++) if (FormAffinity.Pick(weights, PolityForm.Kingdom, (i + 0.5) / 1000) == PolityForm.Kingdom) kingdoms++;
        Assert.InRange(kingdoms, 450, 550);   // 3 of (3 + 1 + 1 + 1) shares
    }

    [Fact]
    public void WeightsOf_IgnoresUnknownNames_AndReadsNothingFromAnEmptyAffinity()
    {
        Assert.Null(FormAffinity.WeightsOf(new CultureDef()));
        Assert.Null(FormAffinity.WeightsOf(new CultureDef { FormAffinity = { ["Republic"] = 50 } }));
        Assert.NotNull(FormAffinity.WeightsOf(new CultureDef { FormAffinity = { ["horde"] = 50 } }));
    }

    [Fact]
    public void NoFounders_LeavesTheDefaultWorldUntouched()
    {
        using var a = Build(42, _ => { });
        using var b = Build(42, c => c.Founders.Clear());
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void PinnedFounders_BecomeThePolitiesTheyDescribe()
    {
        using var w = Build(42, c =>
        {
            c.Peoples.Add(new HistoryPeople { FolkKey = "elf_wood", CultureKey = "elven_wood" });
            c.Founders.Add(new HistoryFounder { FolkKey = "elf_wood", CultureKey = "elven_wood", Form = PolityForm.LooseAssociation });
            c.Founders.Add(new HistoryFounder { FolkKey = "orc", CultureKey = "orcish_horde", Form = PolityForm.Horde });
        });
        var p = w.History.Polities;
        Assert.True(p.Count >= 2);
        Assert.Equal(("elf_wood", "elven_wood", PolityForm.LooseAssociation), (p[0].FolkKey, p[0].CultureKey, p[0].Form));
        Assert.Equal(("orc", "orcish_horde", PolityForm.Horde), (p[1].FolkKey, p[1].CultureKey, p[1].Form));
    }

    [Fact]
    public void PinnedLocation_PutsTheCapitalOnTheNearestLand()
    {
        using var w = Build(42, c => c.Founders.Add(new HistoryFounder { Latitude = 10, Longitude = 20 }));
        var cap = w.History.Polities[0].CapitalTile;
        var vectors = w.Map.DataStore.GetTileVectors();
        var target = Core.Primitives.Vector3D.FromGeoCoord(new Core.Primitives.GeoCoord(10, 20));
        var elev = w.Map.DataStore.GetSpan<Core.Elevation.ElevationInfo>();
        double best = double.NegativeInfinity;
        for (int i = 0; i < elev.Length; i++)
            if (elev[i].Height >= HistoryTerritory.LandThreshold) best = Math.Max(best, Core.Primitives.Vector3D.Dot(vectors[i], target));
        Assert.Equal(best, Core.Primitives.Vector3D.Dot(vectors[cap], target), 9);   // no land tile is closer
    }

    [Fact]
    public void FolkAlone_TakesTheRostersCultureForIt_AndAFolkWithNoneIsRejected()
    {
        using var w = Build(7, c =>
        {
            c.Peoples.Add(new HistoryPeople { FolkKey = "dogfolk", CultureKey = "dogfolk_pack" });
            c.Founders.Add(new HistoryFounder { FolkKey = "dogfolk" });
        });
        Assert.Equal("dogfolk_pack", w.History.Polities[0].CultureKey);
        Assert.Throws<InvalidDataException>(() => Build(7, c => c.Founders.Add(new HistoryFounder { FolkKey = "gnome" })));
    }

    [Fact]
    public void MoreFoundersThanPolities_IsRejected()
        => Assert.Throws<InvalidOperationException>(() => Build(7, c =>
        {
            c.MaxPolities = 1;
            c.Founders.Add(new HistoryFounder());
            c.Founders.Add(new HistoryFounder());
        }));

    [Fact]
    public void AffinityFormsAreDeterministic()
    {
        void Roster(HistoryContext c) { foreach (var (r, cu) in Races.Zip(Cultures)) c.Peoples.Add(new HistoryPeople { FolkKey = r, CultureKey = cu }); }
        using var a = Build(11, Roster);
        using var b = Build(11, Roster);
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
        Assert.Contains(a.History.Polities, p => Races.Contains(p.FolkKey));
    }
}
