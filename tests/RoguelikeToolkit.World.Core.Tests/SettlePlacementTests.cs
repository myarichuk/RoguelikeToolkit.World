using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.10: history weighs the scorer's candidates (scars, ruins, routes) instead of taking the first few.</summary>
public class SettlePlacementTests
{
    private static CitySiteScore Site(int tile, double score) => new() { TileIndex = tile, Score = score };

    private static (World World, HistoryCatalog History, TurnView View) Fixture(Action<HistoryCatalog> edit)
    {
        var w = new WorldBuilder().WithSize(3).WithSeed(2).WithUserEdits().WithHistory(c => c.HazardRate = 0).Build();
        var h = w.History;
        h.Scars.Clear();
        h.Roads.Clear();
        edit(h);
        return (w, h, new TurnView(w.Map, h, new HistoryContext(), 30));
    }

    [Fact]
    public void BlastedGround_IsAvoided_AndRuinsAreReoccupied()
    {
        var (w, h, _) = Fixture(_ => { });
        using var world = w;
        int n = world.Map.DataStore.TileCount;
        int a = 10, b = n / 2, c = n - 10;
        h.Scars.Add(new HistoryScar { TileIndex = a, PolityId = 0, Kind = HistoryScar.Crater, FoundedTurn = 1 });
        h.Scars.Add(new HistoryScar { TileIndex = c, PolityId = 0, Kind = HistoryScar.Ruin, FoundedTurn = 1 });
        var view = new TurnView(world.Map, h, new HistoryContext(), 30);
        var vectors = world.Map.DataStore.GetTileVectors();
        var items = new List<CitySiteScore> { Site(a, 1.0), Site(b, 0.9), Site(c, 0.8) };

        var ranked = SettleOperator.BestByHistory(view, items, s => s, vectors);

        Assert.Equal(new[] { c, b, a }, ranked.Select(s => s.TileIndex).ToArray());
    }

    [Fact]
    public void WithoutHistory_ScorerOrderIsKept()
    {
        var (w, h, view) = Fixture(_ => { });
        using var world = w;
        int n = world.Map.DataStore.TileCount;
        var items = new List<CitySiteScore> { Site(10, 1.0), Site(n / 4, 0.9), Site(n / 2, 0.8), Site(n - 10, 0.7) };
        var ranked = SettleOperator.BestByHistory(view, items, s => s, world.Map.DataStore.GetTileVectors());
        Assert.Equal(new[] { 10, n / 4, n / 2 }, ranked.Select(s => s.TileIndex).ToArray());
    }
}
