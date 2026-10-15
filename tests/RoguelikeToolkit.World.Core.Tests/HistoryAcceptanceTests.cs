using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M1 acceptance on the standard ruleset wired through <see cref="WorldBuilder"/>.</summary>
public class HistoryAcceptanceTests
{
    private static readonly int[] Seeds = { 42, 7, 1234 };

    private static World Build(int seed, int? turns = null, Action<HistoryDriver>? driver = null)
    {
        var b = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits();
        if (turns is int t) b.WithHistory(c => c.TurnCount = t);
        if (driver != null) b.WithHistoryDriver(driver);
        return b.Build();
    }

    private static string Sig(HistoryEvent e) => $"{e.Id}|{e.Kind}|{e.Turn}|{e.Cause}|{e.PolityId}|{e.TileIndex}|{string.Join(',', e.Tags)}";

    [Fact]
    public void StandardRuleset_IsDeterministic_AndRoundTrips()
    {
        foreach (int seed in Seeds)
        {
            using var a = Build(seed);
            using var b = Build(seed);
            Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
            Assert.NotEmpty(a.History.Events);
            using var s = new MemoryStream(a.History.ToBytes(), writable: false);
            Assert.Equal(a.History.ToBytes(), HistoryCatalog.Load(s).ToBytes());
        }
    }

    [Fact]
    public void ShorterRun_ProducesThePrefixOfALongerRun()
    {
        foreach (int seed in Seeds)
        {
            using var shortRun = Build(seed, 12);
            using var longRun = Build(seed, 30);
            var expected = longRun.History.Events.Where(e => e.Turn > 0 && e.Turn < 12 && e.Kind != HistoryEventKind.Myth).Select(Sig).ToList();
            var actual = shortRun.History.Events.Where(e => e.Turn > 0 && e.Turn < 12 && e.Kind != HistoryEventKind.Myth).Select(Sig).ToList();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, actual);
        }
    }
}
