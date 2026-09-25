using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

public class NoiseTests
{
    [Fact]
    public void Value_StaysInRange()
    {
        var r = Rng.Create(7, 0);
        for (int i = 0; i < 500; i++)
        {
            var p = new Vector3D(r.NextDouble() * 8 - 4, r.NextDouble() * 8 - 4, r.NextDouble() * 8 - 4);
            double v = SphereNoise.Value(p, 42);
            Assert.InRange(v, -1.0, 1.0);
        }
    }

    [Fact]
    public void Value_IsDeterministicForSameInput()
    {
        var p = new Vector3D(1.3, -2.1, 0.7);
        Assert.Equal(SphereNoise.Value(p, 42), SphereNoise.Value(p, 42));
    }

    [Fact]
    public void Value_DiffersAcrossSeeds()
    {
        var p = new Vector3D(1.3, -2.1, 0.7);
        Assert.NotEqual(SphereNoise.Value(p, 42), SphereNoise.Value(p, 43));
    }

    [Fact]
    public void Value_MatchesGoldenSamples()
    {
        // Change detectors: any intentional algorithm change must update these.
        Assert.Equal(0.55393570816033444, SphereNoise.Value(new Vector3D(1.3, -2.1, 0.7), 42), 12);
        Assert.Equal(0.17754048217538601, SphereNoise.Fbm(new Vector3D(0.5, 0.5, 0.5), 42), 12);
    }

    [Fact]
    public void Fbm_StaysInRange()
    {
        var r = Rng.Create(99, 0);
        for (int i = 0; i < 200; i++)
        {
            var p = new Vector3D(r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2);
            Assert.InRange(SphereNoise.Fbm(p, 42), -1.0, 1.0);
        }
    }
}
