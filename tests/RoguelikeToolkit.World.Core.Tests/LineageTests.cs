using RoguelikeToolkit.World.Core.Lineage;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class LineageTests
{
    [Fact]
    public void HalfAndHalf_ResolvesMixed()
    {
        var ancestry = Ancestry.From(new AncestryShare("human", 0.5), new AncestryShare("elf_high", 0.5));
        Assert.Equal(Ancestry.Mixed, ancestry.ResolveImpliedStatus());
        Assert.Equal("human", ancestry.Dominant);
    }

    [Fact]
    public void DominantShare_ResolvesFolk()
    {
        var ancestry = Ancestry.From(new AncestryShare("dwarf", 0.75), new AncestryShare("human", 0.25));
        Assert.Equal("dwarf", ancestry.ResolveImpliedStatus());
        var lone = Ancestry.From(new AncestryShare("orc", 1.0));
        Assert.Equal("orc", lone.ResolveImpliedStatus());
    }

    [Fact]
    public void CustomThreshold_MovesTheLine()
    {
        var ancestry = Ancestry.From(new AncestryShare("human", 0.6), new AncestryShare("elf_high", 0.4));
        Assert.Equal("human", ancestry.ResolveImpliedStatus(0.5));
        Assert.Equal(Ancestry.Mixed, ancestry.ResolveImpliedStatus(0.75));
    }

    [Fact]
    public void SterilePair_RejectsSymmetrically()
    {
        var matrix = new CompatibilityMatrix().With("demon", "angel", CompatibilityRule.Sterile);
        Assert.False(matrix.TryUnion("demon", "angel", out var rule));
        Assert.Equal(CompatibilityRule.Sterile, rule);
        Assert.False(matrix.TryUnion("angel", "demon", out _));
    }

    [Fact]
    public void UnlistedAndSelfPairs_Blend()
    {
        var matrix = new CompatibilityMatrix().With("demon", "angel", CompatibilityRule.Sterile);
        Assert.True(matrix.TryUnion("human", "dwarf", out var rule));
        Assert.Equal(CompatibilityRule.Blend, rule);
        Assert.True(matrix.TryUnion("human", "human", out _));
    }

    [Fact]
    public void DominantRule_RoundTrips()
    {
        var matrix = new CompatibilityMatrix().With("elf_high", "human", CompatibilityRule.Dominant);
        Assert.Equal(CompatibilityRule.Dominant, matrix.Resolve("human", "elf_high"));
    }
}
