using Xunit;
using RoguelikeToolkit.Planet.Core;
using System;

namespace RoguelikeToolkit.Planet.Core.Tests;

public class ProjectionTests
{
    private const double Tolerance = 1e-6;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(-45, -45)]
    [InlineData(89.4, 179)] // Test extremes below Mercator limits
    public void Mercator_ProjectAndInverse_ShouldReturnOriginalCoord(double lat, double lon)
    {
        var projection = new MercatorProjection();
        var original = new GeoCoord(lat, lon);
        var projected = projection.Project(original);
        var inverted = projection.Inverse(projected);

        Assert.Equal(original.Latitude, inverted.Latitude, 5);
        Assert.Equal(original.Longitude, inverted.Longitude, 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(-45, -45)]
    [InlineData(90, 180)]
    public void Equirectangular_ProjectAndInverse_ShouldReturnOriginalCoord(double lat, double lon)
    {
        var projection = new EquirectangularProjection();
        var original = new GeoCoord(lat, lon);
        var projected = projection.Project(original);
        var inverted = projection.Inverse(projected);

        Assert.Equal(original.Latitude, inverted.Latitude, 5);
        Assert.Equal(original.Longitude, inverted.Longitude, 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(-10, -10)]
    [InlineData(45, 45)]
    public void Gnomonic_ProjectAndInverse_ShouldReturnOriginalCoord(double lat, double lon)
    {
        var projection = new GnomonicProjection(1.0, new GeoCoord(0, 0));
        var original = new GeoCoord(lat, lon);
        var projected = projection.Project(original);
        var inverted = projection.Inverse(projected);

        Assert.Equal(original.Latitude, inverted.Latitude, 5);
        Assert.Equal(original.Longitude, inverted.Longitude, 5);
    }
}
