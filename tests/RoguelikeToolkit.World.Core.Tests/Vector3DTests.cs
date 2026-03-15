using Xunit;
using RoguelikeToolkit.World.Core;
using System;

namespace RoguelikeToolkit.World.Core.Tests;

public class Vector3DTests
{

    [Fact]
    public void Normalize_ValidVector_ReturnsVectorWithLengthOne()
    {
        var v = new Vector3D(1, 2, 3);
        var normalized = v.Normalize();

        Assert.Equal(1.0, normalized.Length, 5);
        Assert.Equal(v.X / v.Length, normalized.X, 5);
        Assert.Equal(v.Y / v.Length, normalized.Y, 5);
        Assert.Equal(v.Z / v.Length, normalized.Z, 5);
    }

    [Fact]
    public void Normalize_ZeroVector_ReturnsZeroVector()
    {
        var v = new Vector3D(0, 0, 0);
        var normalized = v.Normalize();

        Assert.Equal(0.0, normalized.Length, 5);
        Assert.Equal(0, normalized.X);
        Assert.Equal(0, normalized.Y);
        Assert.Equal(0, normalized.Z);
    }
}
