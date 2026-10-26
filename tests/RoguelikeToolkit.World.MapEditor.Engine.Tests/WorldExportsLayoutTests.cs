using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

/// <summary>
/// The exports are a wire format: flat typed arrays whose layout the TypeScript side indexes by hand
/// (<c>c[i * 12 + 6]</c>). A length or stride drifting here is silent corruption in the editor, so pin them.
/// </summary>
public class WorldExportsLayoutTests
{
    private static int Tiles() => WorldExports.Generate(7, 2, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4);

    [Fact]
    public void PerTileArrays_HaveTheirDocumentedStride()
    {
        int n = Tiles();
        Assert.Equal(n * 3, WorldExports.TileVectors().Length);
        Assert.Equal(n, WorldExports.Heights().Length);
        Assert.Equal(n, WorldExports.Biomes().Length);
        Assert.Equal(n, WorldExports.Plates().Length);
        Assert.Equal(n, WorldExports.Flow().Length);
        Assert.Equal(n, WorldExports.LakeDepth().Length);
        Assert.Equal(n * 2, WorldExports.Climate().Length);
        Assert.Equal(n * 12, WorldExports.ClimateField().Length);
        Assert.Equal(n * 5, WorldExports.LandCapacity().Length);
        Assert.Equal(n * 3, WorldExports.LandUse().Length);
    }

    [Fact]
    public void TileVectors_AreUnitLength()
    {
        Tiles();
        var v = WorldExports.TileVectors();
        for (int i = 0; i < v.Length; i += 3)
            Assert.InRange(Math.Sqrt(v[i] * v[i] + v[i + 1] * v[i + 1] + v[i + 2] * v[i + 2]), 0.999, 1.001);
    }

    [Fact]
    public void Faces_AreTriplesOfValidTileIndices()
    {
        int n = Tiles();
        var f = WorldExports.Faces();
        Assert.True(f.Length > 0);
        Assert.Equal(0, f.Length % 3);
        foreach (var t in f) Assert.InRange(t, 0, n - 1);
    }

    [Fact]
    public void SeaIce_IsOneByteOfZeroToTwoPerTile_AndNeverOnLand()
    {
        int tiles = Tiles();
        var ice = WorldExports.SeaIce();
        var heights = WorldExports.Heights();
        Assert.Equal(tiles, ice.Length);
        for (int i = 0; i < ice.Length; i++)
        {
            Assert.InRange(ice[i], (byte)0, (byte)2);
            if (heights[i] >= 0.0) Assert.Equal(0, ice[i]);
        }
    }

    [Fact]
    public void Climate_IsTheLeadingPairOfClimateField()
    {
        int n = Tiles();
        var c = WorldExports.Climate();
        var f = WorldExports.ClimateField();
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(c[i * 2], f[i * 12]);
            Assert.Equal(c[i * 2 + 1], f[i * 12 + 1]);
        }
    }

    [Fact]
    public void ClimateField_EndsWithTheRainPhase_AndItsMagnitudeIsTheRainSeasonality()
    {
        int n = Tiles();
        var f = WorldExports.ClimateField();
        double widest = 0;
        bool sawWinter = false, sawSummer = false;
        for (int i = 0; i < n; i++)
        {
            double seasonality = f[i * 12 + 6], phase = f[i * 12 + 11];
            Assert.InRange(phase, -1.0, 1.0);
            Assert.Equal(seasonality, Math.Abs(phase), 5); // float32 storage on both columns
            widest = Math.Max(widest, Math.Abs(phase));
            sawWinter |= phase > 0.3; sawSummer |= phase < -0.3;
        }
        Assert.True(widest > 0.5, "an Earth-tilt world has strongly seasonal rain somewhere");
        Assert.True(sawWinter && sawSummer, "and both regimes: monsoon (summer) and Mediterranean (winter)");
    }

    [Fact]
    public void ClimateField_IsFinite()
    {
        Tiles();
        foreach (var x in WorldExports.ClimateField()) Assert.True(double.IsFinite(x));
    }

    [Fact]
    public void Rivers_CountMatchesTheRecordsThatFollow()
    {
        int n = Tiles();
        var a = WorldExports.Rivers();
        int pos = 1;
        for (int r = 0; r < a[0]; r++)
        {
            int len = a[pos + 3];
            for (int k = 0; k < len; k++) Assert.InRange(a[pos + 4 + k], 0, n - 1);
            pos += 4 + len;
        }
        Assert.Equal(a.Length, pos);
    }

    [Fact]
    public void WaterBodiesAndRanges_CountMatchesTheRecordsThatFollow()
    {
        int n = Tiles();
        foreach (var a in new[] { WorldExports.WaterBodies(), WorldExports.Ranges() })
        {
            int pos = 1;
            for (int r = 0; r < a[0]; r++)
            {
                int len = a[pos + 2];
                for (int k = 0; k < len; k++) Assert.InRange(a[pos + 3 + k], 0, n - 1);
                pos += 3 + len;
            }
            Assert.Equal(a.Length, pos);
        }
    }

    [Fact]
    public void Deposits_AreFourIntsEachAndPointAtRealTiles()
    {
        int n = Tiles();
        var d = WorldExports.Deposits();
        Assert.Equal(1 + d[0] * 4, d.Length);
        for (int i = 0; i < d[0]; i++) Assert.InRange(d[3 + i * 4], 0, n - 1);
    }

    [Fact]
    public void PlateDrift_IsFourDoublesPerPlate()
    {
        Tiles();
        Assert.Equal(0, WorldExports.PlateDrift().Length % 4);
        Assert.True(WorldExports.PlateDrift().Length > 0);
    }

    [Fact]
    public void CitySiteReasons_LineUpWithTheSitesAndExplainTheirScores()
    {
        Tiles();
        var sites = WorldExports.CitySites(40);
        var reasons = WorldExports.CitySiteReasons(40);
        Assert.NotEmpty(reasons);
        Assert.Equal(sites.Length / 2, reasons.Length);
        var weight = new System.Collections.Generic.Dictionary<string, double>
        {
            ["water"] = SettlementScoring.FreshWater, ["fertile"] = SettlementScoring.Fertile, ["harsh"] = SettlementScoring.Harsh,
            ["winter-ice"] = SettlementScoring.WinterIce, ["high"] = SettlementScoring.HighGround,
            ["river"] = SettlementScoring.River, ["fishing"] = SettlementScoring.Fishing,
        };
        for (int i = 0; i < reasons.Length; i++)
        {
            var terms = reasons[i].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Contains("history") || terms.Contains("past-events")) continue;   // those add terms the list does not weigh
            double named = SettlementScoring.Base + terms.Sum(t => weight[t]);
            // What the named terms do not account for is danger, at 0.2 a step and never a bonus: a reason list that names a
            // term the score did not take (or omits one it did) leaves a remainder that is not a whole number of danger steps.
            double gap = (named - sites[i * 2 + 1] / 1000.0) / SettlementScoring.PerDanger;
            Assert.True(gap > -0.01 && Math.Abs(gap - Math.Round(gap)) < 0.02, $"site {i}: reasons '{reasons[i]}' leave {gap:F3} danger steps");
        }
    }

    [Fact]
    public void CitySites_AreCappedSortedAndInRange()
    {
        int n = Tiles();
        var s = WorldExports.CitySites(5);
        Assert.Equal(0, s.Length % 2);
        Assert.True(s.Length / 2 <= 5);
        for (int i = 0; i < s.Length; i += 2)
        {
            Assert.InRange(s[i], 0, n - 1);
            if (i >= 2) Assert.True(s[i + 1] <= s[i - 1], "best first");
        }
    }

    [Fact]
    public void NameStyles_AreTabSeparatedTriples()
    {
        foreach (var s in WorldExports.NameStyles()) Assert.Equal(3, s.Split('\t').Length);
    }

    [Fact]
    public void HistoryArrays_HaveTheirDocumentedStride_AndAlignWithTheirStrings()
    {
        WorldExports.ResetHistoryConfig();
        int n = Tiles();
        var info = WorldExports.HistoryInfo();
        Assert.Equal(WorldExports.HistoryInfoLength, info.Length);
        Assert.Equal(1, info[0]);
        int polities = info[3], settlements = info[4], roads = info[5], scars = info[6], events = info[7], changes = info[8], scalars = info[9], samples = info[10];
        Assert.True(polities > 0 && settlements > 0 && events > 0 && samples > 0);

        Assert.Equal(n, WorldExports.HistoryOwnershipAt(0).Length);
        Assert.Equal(n, WorldExports.HistoryOwnershipAt(info[1]).Length);
        Assert.Equal(n, WorldExports.HistoryOwnershipBase().Length);
        Assert.Equal(changes * WorldExports.TerritoryChangeStride, WorldExports.HistoryTerritoryChanges().Length);
        Assert.Equal(polities * WorldExports.PolityStride, WorldExports.HistoryPolities().Length);
        Assert.Equal(polities, WorldExports.HistoryPolityKeys().Length);
        Assert.All(WorldExports.HistoryPolityKeys(), k => Assert.Equal(7, k.Split('\t').Length));   // folk, culture, overlay, name, name race, name culture, architecture
        Assert.Equal(settlements * WorldExports.SettlementStride, WorldExports.HistorySettlements().Length);
        Assert.Equal(settlements, WorldExports.HistorySettlementEndCauses().Length);
        Assert.Equal(roads * WorldExports.RoadStride, WorldExports.HistoryRoads().Length);
        Assert.Equal(scars * WorldExports.ScarStride, WorldExports.HistoryScars().Length);
        Assert.Equal(scars, WorldExports.HistoryScarKinds().Length);
        Assert.All(WorldExports.HistoryScarKinds(), k => Assert.Equal(2, k.Split('\t').Length));

        var series = WorldExports.HistoryPolitySeries();
        Assert.Equal(scalars, (int)series[0]);
        Assert.Equal(1 + samples * (3 + scalars), series.Length);
    }

    [Fact]
    public void ExpansionArrays_HaveTheirDocumentedStride_AndPointAtRealThings()
    {
        WorldExports.ResetHistoryConfig();
        WorldExports.ConfigureHistoryFeatures((int)RoguelikeToolkit.World.Core.History.HistoryFeatures.All);
        try
        {
            Tiles();
            var info = WorldExports.HistoryInfo();
            int persons = info[11], relations = info[12], tenures = info[13], battles = info[14], routes = info[15];
            Assert.True(persons > 0 && tenures > 0 && battles > 0 && routes > 0);
            int strings = WorldExports.HistoryStrings().Length;
            var p = WorldExports.HistoryPersons();
            Assert.Equal(persons * WorldExports.PersonStride, p.Length);
            Assert.Equal(persons, WorldExports.HistoryPersonText().Length);
            Assert.All(WorldExports.HistoryPersonText(), t => Assert.Equal(3, t.Split('\t').Length));
            var kinds = WorldExports.HistoryPersonKindNames();
            for (int i = 0; i < persons; i++)
            {
                int o = i * WorldExports.PersonStride;
                Assert.Equal(i, p[o]);
                Assert.False(string.IsNullOrEmpty(kinds[p[o + 1]]));
                Assert.InRange(p[o + 10], -1, strings - 1);
            }
            Assert.Equal(relations * WorldExports.RelationStride, WorldExports.HistoryRelations().Length);
            var t = WorldExports.HistoryTenures();
            Assert.Equal(tenures * WorldExports.TenureStride, t.Length);
            for (int i = 0; i < tenures; i++) Assert.InRange(t[i * WorldExports.TenureStride], 0, persons - 1);
            var b = WorldExports.HistoryBattles();
            Assert.Equal(battles * WorldExports.BattleStride, b.Length);
            Assert.Equal(battles, WorldExports.HistoryBattleText().Length);
            Assert.All(WorldExports.HistoryBattleText(), x => Assert.Equal(6, x.Split('\t').Length));
            var outcomes = WorldExports.HistoryBattleOutcomeNames();
            for (int i = 0; i < battles; i++) Assert.False(string.IsNullOrEmpty(outcomes[b[i * WorldExports.BattleStride + 7]]));
            Assert.Equal(routes * WorldExports.RouteStateStride, WorldExports.HistoryRouteStates().Length);
            Assert.Equal(0, WorldExports.HistoryRouteSeries().Length % WorldExports.RouteSampleStride);
            Assert.Contains(WorldExports.HistoryFeatureNames(), n => n.StartsWith("Battles\t", StringComparison.Ordinal));
        }
        finally
        {
            WorldExports.ResetHistoryConfig();
        }
    }

    [Fact]
    public void HistoryEvents_DecodeWithACursor_AndMatchTheirTextPage()
    {
        WorldExports.ResetHistoryConfig();
        int n = Tiles();
        int total = WorldExports.HistoryInfo()[7];
        int polities = WorldExports.HistoryInfo()[3];
        var kinds = WorldExports.HistoryEventKindNames();
        int strings = WorldExports.HistoryStrings().Length;
        var page = WorldExports.HistoryEvents(0, total);
        Assert.Equal(total, page[0]);
        Assert.Equal(total, WorldExports.HistoryEventText(0, total).Length);
        int c = 1, lastId = -1;
        for (int i = 0; i < total; i++)
        {
            int id = page[c], kind = page[c + 1], polity = page[c + 3], tile = page[c + 4], cause = page[c + 5], parts = page[c + 7];
            Assert.True(id > lastId, "log order is id order");
            lastId = id;
            Assert.False(string.IsNullOrEmpty(kinds[kind]));
            Assert.InRange(polity, -1, polities - 1);
            Assert.InRange(tile, -1, n - 1);
            Assert.InRange(cause, -1, id - 1);
            c += 8;
            for (int k = 0; k < parts; k++, c += 2)
            {
                Assert.InRange(page[c], 0, polities - 1);
                Assert.InRange(page[c + 1], 0, strings - 1);
            }
        }
        Assert.Equal(page.Length, c);
    }
}
