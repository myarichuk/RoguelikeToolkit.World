using RoguelikeToolkit.World.MapEditor.Engine;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

// CitySites and CitySiteReasons are two views of one site search: the pairs and the reasons
// must line up, repeat calls must agree, and TopN must be honored per call.
public class WorldExportsSiteCacheTests
{
    private static void Generate(int seed = 7)
        => WorldExports.Generate(seed, 2, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4);

    [Fact]
    public void Sites_And_Reasons_LineUp_And_RepeatCallsAgree()
    {
        Generate();
        var sites = WorldExports.CitySites(100);
        var reasons = WorldExports.CitySiteReasons(100);
        Assert.Equal(sites.Length / 2, reasons.Length);
        Assert.Equal(sites, WorldExports.CitySites(100));
        Assert.Equal(reasons, WorldExports.CitySiteReasons(100));
    }

    [Fact]
    public void TopN_IsHonored_PerCall()
    {
        Generate();
        var few = WorldExports.CitySites(5);
        var many = WorldExports.CitySites(4000);
        Assert.True(few.Length / 2 <= 5, $"expected at most 5 sites, got {few.Length / 2}");
        Assert.True(many.Length >= few.Length, $"expected the full search to cover the TopN one ({many.Length} vs {few.Length})");
    }

    [Fact]
    public void ApplyEdits_DropsTheCache_And_TheWorldStillAnswers()
    {
        Generate();
        var before = WorldExports.CitySites(100);
        WorldExports.ApplyEdits([], [], [], [], [], []);
        Assert.Equal(before, WorldExports.CitySites(100));
        Assert.Equal(before.Length / 2, WorldExports.CitySiteReasons(100).Length);
    }
}
