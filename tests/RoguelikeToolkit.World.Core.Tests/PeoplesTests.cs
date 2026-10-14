using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.Lineage;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class PeoplesTests
{
    [Fact]
    public void Load_ReturnsTwoSampleFolks()
    {
        var store = PeoplesStore.Load();
        var human = store.GetFolk("human");
        Assert.Equal(80, human.LifespanYears);
        Assert.Equal(16, human.MaturationYears);
        Assert.True(human.IsFullPerson);
        var dwarf = store.GetFolk("dwarf");
        Assert.Equal(350, dwarf.LifespanYears);
        Assert.Equal(50, dwarf.MaturationYears);
        Assert.Equal("mountain", dwarf.HabitatAffinity);
    }

    [Fact]
    public void Load_ReturnsTwoSampleCultures()
    {
        var store = PeoplesStore.Load();
        Assert.Equal("fantasy_common", store.GetCulture("human_common").NamingTable);
        var clan = store.GetCulture("dwarven_clan");
        Assert.Equal("dwarven", clan.NamingTable);
        Assert.Equal("ancestor", clan.FaithForm);
    }

    [Fact]
    public void CompatRow_RejectsFromOneYamlRow()
    {
        var store = PeoplesStore.Load();
        Assert.False(store.Compatibility.TryUnion("demon", "angel", out var rule));
        Assert.Equal(CompatibilityRule.Sterile, rule);
        Assert.True(store.Compatibility.TryUnion("human", "dwarf", out _));
    }

    [Fact]
    public void FolkKeys_IndexCoversSamples()
    {
        var store = PeoplesStore.Load();
        Assert.Contains("human", store.FolkKeys);
        Assert.Contains("dwarf", store.FolkKeys);
    }

    [Fact]
    public void Lookups_AreCaseInsensitiveAndHelpful()
    {
        var store = PeoplesStore.Load();
        Assert.Equal("human", store.GetFolk("HUMAN").Key);
        var ex = Assert.Throws<UnknownPeoplesKeyException>(() => store.GetFolk("troll"));
        Assert.Equal("folk", ex.Kind);
        Assert.Contains("human", ex.Available);
    }

    [Fact]
    public void FolkPriors_MapToTraitWeights()
    {
        var store = PeoplesStore.Load();
        var weights = store.GetFolk("dwarf").ToTraitWeights();
        Assert.Equal(5, weights.Length);
        Assert.Equal(TraitWeight.Low, weights[0]);
        Assert.Equal(TraitWeight.High, weights[1]);
    }
}
