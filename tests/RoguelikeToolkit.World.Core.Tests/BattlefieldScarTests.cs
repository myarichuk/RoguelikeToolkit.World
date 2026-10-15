using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Integration;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.1: war battlefields are scars, not a separate site path.</summary>
public class BattlefieldScarTests
{
    private static World Build(int seed)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory(c => c.HazardRate = 3).Build();

    [Fact]
    public void WarTiles_CarryABattlefieldScar_ThatProjectsAsABattlefield()
    {
        int checkedTiles = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            foreach (var war in h.Events.Where(e => e.Kind == HistoryEventKind.War))
            {
                if (h.Sites.Any(s => s.TileIndex == war.TileIndex)) continue;
                var scar = h.Scars.First(s => s.TileIndex == war.TileIndex && s.Kind == HistoryScar.Battlefield);
                Assert.Equal("war", scar.Cause);
                Assert.True(h.TryGetTileModifier(war.TileIndex, out var m));
                Assert.Equal(HistoryCatalog.WarDangerDelta, m.DangerDelta);
                var projected = h.GetSitesNear(scar.Position, 1).First(x => x.TileIndex == war.TileIndex && x.Kind == HistoricSiteKind.Battlefield);
                Assert.True(projected.Metadata.ContainsKey("battle"));
                checkedTiles++;
            }
            Assert.Equal(h.Scars.Count(s => s.Kind == HistoryScar.Battlefield), h.Scars.Where(s => s.Kind == HistoryScar.Battlefield).Select(s => s.TileIndex).Distinct().Count());
        }
        Assert.True(checkedTiles > 0, "No war fought off a capital tile in 3 seeds.");
    }

    [Fact]
    public void BattlefieldAndRuin_OnTheSameTileAndTurn_BothLand()
    {
        var a = new Proposal.AddScar { Tile = 5, ScarKind = HistoryScar.Battlefield };
        var b = new Proposal.AddScar { Tile = 5, ScarKind = HistoryScar.Ruin };
        var c = new Proposal.AddScar { Tile = 5, ScarKind = HistoryScar.Ruin };
        Assert.NotEqual(a.ConflictKey, b.ConflictKey);
        Assert.Equal(b.ConflictKey, c.ConflictKey);
    }
}
