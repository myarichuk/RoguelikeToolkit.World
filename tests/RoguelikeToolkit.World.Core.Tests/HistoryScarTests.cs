using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Integration;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.7: shocks leave scars that persist, project as sites and tile modifiers, and round-trip.</summary>
public class HistoryScarTests
{
    private sealed class ShockOp : IHistoryOperator
    {
        private readonly Func<Shock> _make;
        public ShockOp(Func<Shock> make) => _make = make;
        public string Name => "ShockOp";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink) => ShockResolver.Resolve(view, sink, _make());
    }

    private static World Bare()
        => new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private static void Turn(World w, int turn, Func<Shock> make)
        => new HistoryDriver().AddOperator(new ShockOp(make)).RunTurn(w.Map, w.History, new HistoryContext { DisplacedDecayTurns = 50 }, turn);

    [Fact]
    public void AbandonedSettlement_LeavesARuin_ThatProjectsAsASite()
    {
        using var w = Bare();
        foreach (var s in w.History.Settlements) s.Population = 100;
        foreach (var p in w.History.Polities) p.Population = 100;
        var site = w.History.Settlements[0];
        site.Tier = SettlementTier.Hamlet;
        site.Role = SettlementRole.Colony;
        Turn(w, 1, () => new Shock { Kind = "flood", Severity = 1, Tile = site.TileIndex });
        Assert.True(site.Ended);
        var scar = Assert.Single(w.History.Scars);
        Assert.Equal(HistoryScar.Ruin, scar.Kind);
        Assert.Equal("flood", scar.Cause);
        Assert.Equal(site.TileIndex, scar.TileIndex);
        var found = w.History.GetSitesNear(scar.Position, 1).Single(x => x.Metadata.ContainsKey("scar"));
        Assert.Equal(HistoricSiteKind.Ruin, found.Kind);
        Assert.Equal("ruin", found.Metadata["scar"]);
        Assert.Equal("flood", found.Metadata["cause"]);
    }

    [Fact]
    public void HardHit_LeavesTheShocksScar_AndItDedupes()
    {
        using var w = Bare();
        foreach (var s in w.History.Settlements) s.Population = 100;
        foreach (var p in w.History.Polities) p.Population = 100;
        var site = w.History.Settlements[0];
        for (int turn = 1; turn <= 30 && w.History.Scars.Count == 0; turn++)
        {
            site.Population = 100;
            Turn(w, turn, () => new Shock { Kind = "volcano", Severity = 1, Tile = site.TileIndex, Scar = HistoryScar.Crater });
        }
        var scar = Assert.Single(w.History.Scars);
        Assert.Equal(HistoryScar.Crater, scar.Kind);
        Assert.True(w.History.TryGetTileModifier(scar.TileIndex, out var mod));
        Assert.Equal(HistoryCatalog.ScarDangerDelta, mod.DangerDelta);
        for (int turn = 31; turn <= 60; turn++)
        {
            site.Population = 100;
            Turn(w, turn, () => new Shock { Kind = "volcano", Severity = 1, Tile = site.TileIndex, Scar = HistoryScar.Crater });
        }
        Assert.Single(w.History.Scars, x => x.Kind == HistoryScar.Crater);
    }

    [Fact]
    public void Scars_RoundTrip()
    {
        using var w = Bare();
        w.History.Scars.Add(new HistoryScar { TileIndex = 5, PolityId = 0, FoundedTurn = 3, Kind = HistoryScar.PlaguePit, Cause = "plague", Position = new Primitives.GeoCoord(10, 20), Active = false });
        byte[] bytes = w.History.ToBytes();
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false)) loaded = HistoryCatalog.Load(stream);
        var t = Assert.Single(loaded.Scars);
        Assert.Equal(5, t.TileIndex);
        Assert.Equal("plague_pit", t.Kind);
        Assert.Equal("plague", t.Cause);
        Assert.Equal(3, t.FoundedTurn);
        Assert.False(t.Active);
        Assert.Equal(bytes, loaded.ToBytes());
    }

    [Fact]
    public void FullHistory_LeavesScars()
    {
        int worldsWithScars = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();
            Assert.Equal(w.History.Scars.Count, w.History.Scars.Select(s => (s.TileIndex, s.Kind)).Distinct().Count());
            if (w.History.Scars.Count > 0) worldsWithScars++;
            foreach (var s in w.History.Settlements.Where(s => s.Ended && s.EndCause.Length > 0))
                Assert.Contains(w.History.Scars, x => x.TileIndex == s.TileIndex && x.Kind == HistoryScar.Ruin);
        }
        Assert.True(worldsWithScars >= 3);
    }
}
