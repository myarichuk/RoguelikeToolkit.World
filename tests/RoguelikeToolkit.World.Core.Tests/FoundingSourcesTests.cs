using RoguelikeToolkit.World.Core.History;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class FoundingSourcesTests
{
    private static string Tag(HistoryEvent e, string prefix)
        => e.Tags.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..] ?? "";

    [Fact]
    public void FullRuns_FoundByEverySource_WithMatchingRoleAndOwnedGround()
    {
        var seen = new HashSet<string>();
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().Build();
            var h = w.History;
            foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.SettlementFounded))
            {
                string why = Tag(e, "why:");
                if (why.Length == 0) continue;
                var s = h.Settlements[int.Parse(Tag(e, "settlement:"))];
                var expected = why switch
                {
                    "garrison" => SettlementRole.Post,
                    "pilgrimage" => SettlementRole.Shrine,
                    "route" => SettlementRole.Node,
                    "sedentarized" => SettlementRole.Colony,
                    _ => throw new Xunit.Sdk.XunitException("unknown why " + why),
                };
                Assert.Equal(expected, s.Role);
                Assert.Equal($"role:{expected}", e.Tags.First(t => t.StartsWith("role:", StringComparison.Ordinal)));
                Assert.True(s.Parent >= 0);
                seen.Add(why);
            }
        }
        Assert.Contains("garrison", seen);
        Assert.Contains("pilgrimage", seen);
    }

    [Fact]
    public void Shrines_StandBesideAnOwnHolySite_AndPostsFaceAGrudge()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().Build();
            var h = w.History;
            // After a collapse a shrine may sit in a successor that owns no holy site of its own.
            if (h.Events.Any(e => e.Kind == HistoryEventKind.PolityCollapsed)) continue;
            foreach (var s in h.Settlements.Where(s => s.Role == SettlementRole.Shrine))
                Assert.Contains(h.HolySites, hs => hs.PolityId == s.PolityId);
            foreach (var s in h.Settlements.Where(s => s.Role == SettlementRole.Post))
                Assert.True(h.Polities.Any(p => p.Id != s.PolityId), "A post needs a rival polity to face.");
        }
    }

    [Fact]
    public void RunsAreDeterministic_WithFoundingSources()
    {
        string Digest(int seed)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().Build();
            return string.Join("|", w.History.Settlements.Select(s => $"{s.TileIndex}:{s.Role}:{s.PolityId}:{s.FoundedTurn}"));
        }
        Assert.Equal(Digest(5), Digest(5));
    }
}
