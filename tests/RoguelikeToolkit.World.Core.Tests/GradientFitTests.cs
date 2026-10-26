using RoguelikeToolkit.World.Core.Hierarchy;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>The local-map slope fit must recover a plane exactly wherever its samples sit relative to the region centre.</summary>
public class GradientFitTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.04, 0.0)]
    [InlineData(-0.03, 0.05)]
    [InlineData(0.06, -0.06)]
    public void PlanarSamples_FitExactly(double cx, double cy)
    {
        var frame = new TangentFrame(new GeoCoord(30, 40), 400, 1e-9);
        const double a = 1.0, b = -0.4, c = 0.3;
        var pts = new (double E, double N, double H)[7];
        pts[0] = (cx, cy, a * cx + b * cy + c);
        for (int k = 0; k < 6; k++)
        {
            double ang = k * Math.PI / 3, e = cx + 0.01 * Math.Cos(ang), n = cy + 0.01 * Math.Sin(ang);
            pts[k + 1] = (e, n, a * e + b * n + c);
        }
        double mean = pts.Average(p => p.H);

        var g = ChildContextDeriver.FitGradient(pts, mean, frame, new ParentContext());

        Assert.Equal(a, Vector3D.Dot(g, frame.East), 9);
        Assert.Equal(b, Vector3D.Dot(g, frame.North), 9);
    }
}
