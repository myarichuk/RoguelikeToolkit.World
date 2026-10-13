using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.MapEditor.Engine.Tests;

public class WorldExportsApplyEditsTests
{
    private static int Tiles() => WorldExports.Generate(7, 2, 0, 23.4, 1, 1, true, 0.3, 0, 1, 2, 4);

    private static int Apply(int tile, double pinHeight = 0, int meta = 0, double dh = 0, double dT = 0, double dP = 0)
        => WorldExports.ApplyEdits([tile], [dh], [pinHeight], [meta], [dT], [dP]);

    private static int Clear() => WorldExports.ApplyEdits([], [], [], [], [], []);

    [Fact]
    public void MismatchedLengths_Throw()
    {
        Tiles();
        Assert.Throws<ArgumentException>(() => WorldExports.ApplyEdits([1, 2], [0.0], [0.0], [0], [0.0], [0.0]));
        Assert.Throws<ArgumentException>(() => WorldExports.ApplyEdits([1], [0.0], [0.0], [0], [0.0], [0.0, 0.0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void TileOutOfRange_Throws(int tile)
    {
        Tiles();
        Assert.Throws<ArgumentOutOfRangeException>(() => Apply(tile));
    }

    [Fact]
    public void ReturnsTheTileCount()
    {
        int n = Tiles();
        Assert.Equal(n, Clear());
    }

    [Fact]
    public void PinHeight_FixesTheTileAfterErosion()
    {
        Tiles();
        Apply(5, pinHeight: 0.8, meta: 1);
        Assert.Equal(0.8, WorldExports.Heights()[5], 3);
    }

    [Fact]
    public void PinBiome_IsUnpackedFromBitsEightToFifteen()
    {
        Tiles();
        byte was = WorldExports.Biomes()[5];
        byte pin = was == 10 ? (byte)3 : (byte)10;
        Apply(5, meta: 2 | (pin << 8));
        Assert.Equal(pin, WorldExports.Biomes()[5]);
    }

    [Fact]
    public void PinLandUse_IsUnpackedFromBitsSixteenToThirtyOne()
    {
        Tiles();
        Apply(5, meta: 4 | (3 << 16) | (5 << 24));
        var u = WorldExports.LandUse();
        Assert.Equal(3, u[5 * 3]);
        Assert.Equal(5, u[5 * 3 + 1]);
    }

    [Fact]
    public void Edits_AreAbsoluteNotAccumulated()
    {
        Tiles();
        Apply(5, pinHeight: 0.8, meta: 1);
        var once = WorldExports.Heights();
        Apply(5, pinHeight: 0.8, meta: 1);
        Assert.Equal(once, WorldExports.Heights());
    }

    [Fact]
    public void EmptyTable_RestoresTheUneditedWorld()
    {
        Tiles();
        var h = WorldExports.Heights();
        var b = WorldExports.Biomes();
        Apply(5, pinHeight: 0.8, meta: 1);
        Assert.NotEqual(h, WorldExports.Heights());
        Clear();
        Assert.Equal(h, WorldExports.Heights());
        Assert.Equal(b, WorldExports.Biomes());
    }

    [Fact]
    public void BrushedClimateOffsets_ReachTheClimateLayer()
    {
        Tiles();
        var before = WorldExports.Climate();
        Apply(5, dT: 0.2, dP: 0.2);
        Assert.NotEqual(before, WorldExports.Climate());
    }

    // The edit table of the live world, read without going through ApplyEdits (which would rewrite it).
    private static UserEditInfo EditAt(int tile)
    {
        var world = (RoguelikeToolkit.World.Core.World)typeof(WorldExports)
            .GetField("_world", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        return world.Edits[tile];
    }

    [Fact]
    public void ARejectedRequest_KeepsThePreviousEdits()
    {
        Tiles();
        Apply(5, pinHeight: 0.8, meta: 1);
        // Tile 5 is valid, tile -1 is not: nothing from this call may land, and the old pin must survive.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldExports.ApplyEdits([7, -1], [0.0, 0.0], [0.3, 0.0], [1, 0], [0.0, 0.0], [0.0, 0.0]));
        Assert.Equal(UserEditFlags.PinHeight, EditAt(5).Flags);
        Assert.Equal(0.8f, EditAt(5).PinHeight);
        Assert.Equal(UserEditFlags.None, EditAt(7).Flags);
    }

    [Theory]
    [InlineData(double.NaN, 0.0, 0.0, 0.0)]
    [InlineData(0.0, double.PositiveInfinity, 0.0, 0.0)]
    [InlineData(0.0, 0.0, double.NaN, 0.0)]
    [InlineData(0.0, 0.0, 0.0, double.NegativeInfinity)]
    public void NonFiniteValues_Throw_AndKeepThePreviousEdits(double dh, double pin, double dT, double dP)
    {
        Tiles();
        Apply(5, pinHeight: 0.8, meta: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldExports.ApplyEdits([6], [dh], [pin], [0], [dT], [dP]));
        Assert.Equal(0.8f, EditAt(5).PinHeight);
    }

    [Fact]
    public void AnUnknownPinnedBiome_Throws()
    {
        Tiles();
        Assert.Throws<ArgumentOutOfRangeException>(() => Apply(5, meta: 2 | (200 << 8)));
    }
}
