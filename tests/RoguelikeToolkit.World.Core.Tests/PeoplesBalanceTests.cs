using RoguelikeToolkit.World.Core.History;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// With the full roster, no people should dominate the map by count of states: founders are drawn without replacement and
/// every culture can rebel, so breakaways no longer multiply only the tight-knit peoples.
/// </summary>
public class PeoplesBalanceTests(ITestOutputHelper output)
{
    private static readonly (string Folk, string Culture)[] Roster =
    {
        ("human", "human_common"), ("dwarf", "dwarven_clan"), ("elf_wood", "elven_wood"), ("elf_high", "elven_high"),
        ("elf_dark", "elven_dark"), ("orc", "orcish_horde"), ("halfling", "halfling_hearth"), ("gnome", "gnomish_burrow"),
        ("lizardfolk", "lizardfolk_marsh"), ("dogfolk", "dogfolk_pack"), ("catfolk", "catfolk_prowl"), ("dragonfolk", "dragonfolk_aerie"),
    };

    [Fact]
    public void FullRoster_NoPeopleDominatesTheStates()
    {
        var count = Roster.ToDictionary(r => r.Folk, _ => 0);
        for (int seed = 1; seed <= 24; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithHistory(c =>
            {
                c.Peoples.Clear();
                foreach (var (folk, culture) in Roster) c.Peoples.Add(new HistoryPeople { FolkKey = folk, CultureKey = culture });
            }).Build();
            foreach (var p in w.History.Polities) count[p.FolkKey]++;
        }
        output.WriteLine(string.Join(", ", count.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        double mean = count.Values.Average();
        Assert.True(count.Values.Max() <= 1.6 * mean, $"most common people has {count.Values.Max()} states, mean {mean:F1}");
        Assert.True(count.Values.Min() >= 0.4 * mean, $"least common people has {count.Values.Min()} states, mean {mean:F1}");
    }
}
