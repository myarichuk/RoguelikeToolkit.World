namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

public class WorldExportsGenerateTests
{
    private static int Generate(double windCellBlend = 0, double meridionalWind = 0.3, double oceanHeat = 1, double oceanRain = 2, int aeolian = 4,
        double obliquity = 23.4, double flux = 1, double rotation = 1)
        => WorldExports.Generate(7, 2, 0, obliquity, flux, rotation, true, meridionalWind, windCellBlend, oceanHeat, oceanRain, aeolian);

    [Fact]
    public void Generate_ReturnsTheTileCount()
        => Assert.True(Generate() > 0);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void WindCellBlend_OutsideZeroToOne_Throws(double blend)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Generate(windCellBlend: blend));
        Assert.Equal("windCellBlend", ex.ParamName);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void WindCellBlend_AtTheBounds_IsAccepted(double blend)
        => Assert.True(Generate(windCellBlend: blend) > 0);

    [Fact]
    public void NegativeStrengths_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(meridionalWind: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(oceanHeat: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(oceanRain: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(aeolian: -1));
    }

    [Fact]
    public void BadPlanetValues_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(obliquity: 91));
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(obliquity: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(flux: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(rotation: double.NaN));
    }

    // A rejected request must leave the previous world fully usable: ApplyEdits re-runs the pipeline on it,
    // which fails with ObjectDisposedException if the rejected call had already disposed it.
    [Theory]
    [InlineData(2.0, 0.3)]
    [InlineData(0.0, -1.0)]
    public void ARejectedCall_LeavesThePreviousWorldUsable(double blend, double wind)
    {
        int tiles = Generate();
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(windCellBlend: blend, meridionalWind: wind));
        Assert.Equal(tiles, WorldExports.ApplyEdits([], [], [], [], [], []));
    }

    [Fact]
    public void TheBlend_ReachesTheClimate()
    {
        Generate(windCellBlend: 0);
        var legacy = WorldExports.Climate();
        Generate(windCellBlend: 1);
        var blended = WorldExports.Climate();
        Assert.Equal(legacy.Length, blended.Length);
        Assert.NotEqual(legacy, blended);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        Generate(windCellBlend: 0.5);
        var a = WorldExports.Climate();
        Generate(windCellBlend: 0.5);
        Assert.Equal(a, WorldExports.Climate());
    }

    [Fact]
    public void ABadSize_KeepsThePreviousWorldUsable()
    {
        int tiles = Generate();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.Generate(7, -1, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4));
        Assert.Equal(tiles, WorldExports.ApplyEdits([], [], [], [], [], []));
    }

    // If the build itself fails the old world is already gone. Exports must say "Generate first", not read a freed store.
    [Fact]
    public void AFailedBuild_LeavesNoDisposedWorldBehind()
    {
        Generate();
        Assert.ThrowsAny<Exception>(() => WorldExports.Generate(7, 2, 1_000_000, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4));
        Assert.Throws<InvalidOperationException>(() => WorldExports.Heights());
        Assert.Throws<InvalidOperationException>(() => WorldExports.Faces());
        Assert.True(Generate() > 0);   // and the editor recovers on the next good request
    }

    [Fact]
    public void NamePlaces_WithMismatchedArrays_ThrowsInsteadOfIndexingPastTheEnd()
    {
        Assert.Throws<ArgumentException>(() => WorldExports.NamePlaces(["town", "city"], [1], ["human", "human"], ["a", "b"], [0, 0]));
        Assert.Throws<ArgumentException>(() => WorldExports.NamePlaces(["town"], [1], ["human"], ["a"], []));
    }

    [Fact]
    public void NamePersons_IsDeterministic_AndNamesNobodyForAnUnknownPeople()
    {
        var a = WorldExports.NamePersons([7, 7, 8, 7], ["human", "human", "human", "nobody"], ["human_common", "human_common", "human_common", "nowhere"], [0, 0, 0, 0]);
        Assert.False(string.IsNullOrEmpty(a[0]));
        Assert.Equal(a[0], a[1]);
        Assert.NotEqual(a[0], a[2]);
        Assert.Equal(string.Empty, a[3]);
        Assert.Throws<ArgumentException>(() => WorldExports.NamePersons([1], ["human"], ["human_common"], []));
    }
}
