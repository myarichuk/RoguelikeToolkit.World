using RoguelikeToolkit.World.MapEditor.Engine;

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
}
