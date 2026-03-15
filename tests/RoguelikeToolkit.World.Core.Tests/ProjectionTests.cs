using Xunit;
using RoguelikeToolkit.World.Core;
using System;

namespace RoguelikeToolkit.World.Core.Tests;

public class ProjectionTests
{
    private const double Tolerance = 1e-6;

    [Theory]
    [InlineData(Math.PI, 180)]
    [InlineData(Math.PI / 2, 90)]
    [InlineData(0, 0)]
    [InlineData(-Math.PI / 2, -90)]
    [InlineData(-Math.PI, -180)]
    public void GeoCoord_FromRadians_ConvertsCorrectly(double rad, double expectedDeg)
    {
        var coord = GeoCoord.FromRadians(rad, rad);
        Assert.Equal(expectedDeg, coord.Latitude, 5);
        Assert.Equal(expectedDeg, coord.Longitude, 5);
    }

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

    [Theory]
    [InlineData(0, 180)]
    [InlineData(90, 180)]
    [InlineData(-90, 180)]
    [InlineData(0, -180)]
    [InlineData(0, 100)] // Center is (0,0), so dLon > 90deg cos(dLon) < 0, cosC < 0
    [InlineData(0, -100)]
    // Math.Cos(90) in radians evaluates to a very small positive number (e.g. 6.12e-17)
    // rather than exactly 0, so we use points definitely on the back half of the sphere.
    [InlineData(0, 179)]
    [InlineData(0, -179)]
    [InlineData(45, 180)]
    [InlineData(-45, 180)]
    public void Gnomonic_Project_BackHalfOfSphere_ShouldReturnNaN(double lat, double lon)
    {
        var projection = new GnomonicProjection(1.0, new GeoCoord(0, 0));
        var original = new GeoCoord(lat, lon);

        var projected = projection.Project(original);

        Assert.True(double.IsNaN(projected.X));
        Assert.True(double.IsNaN(projected.Y));
    }
}
