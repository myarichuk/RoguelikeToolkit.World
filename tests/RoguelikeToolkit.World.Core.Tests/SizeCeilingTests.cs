using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Size-ceiling guardrails (backlog A2): 0..MaxSupportedSize build, anything
/// beyond (or negative) throws at configuration time with the ceiling named.
/// </summary>
public class SizeCeilingTests
{
    [Fact]
    public void WithSize_BeyondCeilingThrowsNamingCeiling()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldBuilder().WithSize(WorldBuilder.MaxSupportedSize + 1));
        Assert.Contains(WorldBuilder.MaxSupportedSize.ToString(), ex.Message);
        Assert.Equal("size", ex.ParamName);
    }

    [Fact]
    public void WithSize_NegativeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldBuilder().WithSize(-1));
    }

    [Fact]
    public void WithSize_CeilingAcceptedAtConfigTime()
    {
        // Validation only — no build (size 7 is Stress-covered in ScaleTests).
        var builder = new WorldBuilder().WithSize(WorldBuilder.MaxSupportedSize);
        Assert.NotNull(builder);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SmallSizes_BuildSuccessfully(int size)
    {
        using var world = new WorldBuilder().WithSize(size).WithSeed(42).Build();
        int expected = 10 * (1 << (2 * size)) + 2;
        Assert.Equal(expected, world.TileCount);
    }
}
