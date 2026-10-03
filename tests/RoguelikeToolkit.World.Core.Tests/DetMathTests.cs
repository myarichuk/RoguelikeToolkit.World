using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

// Millions of libm comparisons: serialized with LayerTests, whose perf smoke test is timing-sensitive.
[Collection("CpuTiming")]
public class DetMathTests
{
    private static double UlpError(double actual, double expected)
    {
        if (double.IsNaN(actual) && double.IsNaN(expected)) return 0;
        double ulp = Math.BitIncrement(Math.Abs(expected)) - Math.Abs(expected);
        return Math.Abs(actual - expected) / ulp;
    }

    private static double MaxUlp(Func<double, double> det, Func<double, double> libm, double lo, double hi, int n = 200_000)
    {
        var rng = new Random(12345);
        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            double x = lo + (hi - lo) * rng.NextDouble();
            worst = Math.Max(worst, UlpError(det(x), libm(x)));
        }
        return worst;
    }

    [Theory]
    [InlineData(-10.0, 10.0)]
    [InlineData(-5000.0, 5000.0)]
    public void SinCos_StayWithinTwoUlpOfLibm(double lo, double hi)
    {
        Assert.True(MaxUlp(DetMath.Sin, Math.Sin, lo, hi) <= 2.0);
        Assert.True(MaxUlp(DetMath.Cos, Math.Cos, lo, hi) <= 2.0);
    }

    [Fact]
    public void ExpLog_StayWithinTwoUlpOfLibm()
    {
        Assert.True(MaxUlp(DetMath.Exp, Math.Exp, -700, 700) <= 2.0);
        Assert.True(MaxUlp(DetMath.Log, Math.Log, 1e-300, 1e300) <= 2.0);
    }

    [Fact]
    public void InverseTrig_StaysWithinEightUlpOfLibm()
    {
        Assert.True(MaxUlp(DetMath.Acos, Math.Acos, -1, 1) <= 8.0);
        Assert.True(MaxUlp(DetMath.Asin, Math.Asin, -1, 1) <= 8.0);
        Assert.True(MaxUlp(DetMath.Atan, Math.Atan, -100, 100) <= 8.0);
        Assert.True(MaxUlp(x => DetMath.Atan2(Math.Sin(3.1 * x), Math.Cos(1.7 * x)),
                           x => Math.Atan2(Math.Sin(3.1 * x), Math.Cos(1.7 * x)), -5, 5) <= 8.0);
    }

    [Fact]
    public void Pow_MatchesLibmToRelativeTolerance()
    {
        var rng = new Random(7);
        for (int i = 0; i < 50_000; i++)
        {
            double x = rng.NextDouble() * 100.0, y = rng.NextDouble() * 4.0 - 1.0;
            double expected = Math.Pow(x, y);
            Assert.True(Math.Abs(DetMath.Pow(x, y) - expected) <= 1e-12 * Math.Abs(expected) + 1e-300, $"pow({x},{y})");
        }
    }

    [Fact]
    public void SpecialValues_FollowIeeeConventions()
    {
        Assert.Equal(0.0, DetMath.Sin(0));
        Assert.Equal(1.0, DetMath.Cos(0));
        Assert.Equal(1.0, DetMath.Exp(0));
        Assert.Equal(0.0, DetMath.Log(1));
        Assert.Equal(0.0, DetMath.Acos(1));
        Assert.Equal(Math.PI, DetMath.Acos(-1));
        Assert.Equal(Math.PI / 2, DetMath.Asin(1));
        Assert.Equal(Math.PI, DetMath.Atan2(0, -1));
        Assert.Equal(Math.PI / 2, DetMath.Atan2(1, 0));
        Assert.True(double.IsNaN(DetMath.Acos(1.0000001)));
        Assert.True(double.IsNaN(DetMath.Sin(double.PositiveInfinity)));
        Assert.True(double.IsNaN(DetMath.Log(-1)));
        Assert.Equal(double.NegativeInfinity, DetMath.Log(0));
        Assert.Equal(double.PositiveInfinity, DetMath.Exp(710));
        Assert.Equal(0.0, DetMath.Exp(-800));
        Assert.Equal(0.0, DetMath.Pow(0, 2));
        Assert.Equal(1024.0, DetMath.Pow(2, 10), 9);
        Assert.Equal(-8.0, DetMath.Pow(-2, 3), 9);
    }

    /// <summary>
    /// Exact bit patterns: any platform whose DetMath differs by even one bit
    /// fails here, which is the whole point of not calling libm.
    /// </summary>
    [Fact]
    public void GoldenBitPatterns_AreIdenticalOnEveryPlatform()
    {
        var unary = new (string Fn, double X, long Bits)[]
        {
            ("Sin", 0.5, 0x3FDEAEE8744B05F0L),
            ("Sin", -7.3, unchecked((long)0xBFEB36C6DC1D7445L)),
            ("Sin", 123.456, unchecked((long)0xBFE9B9DADC41AEB5L)),
            ("Cos", 0.5, 0x3FEC1528065B7D50L),
            ("Cos", -7.3, 0x3FE0D5A0848A01CCL),
            ("Cos", 123.456, unchecked((long)0xBFE307E5980A1559L)),
            ("Exp", -3.3, 0x3FA2E259BB85BE85L),
            ("Exp", 0.1, 0x3FF1AEC7B35A00D4L),
            ("Exp", 41.7, 0x43B1E19FB4D6EDF1L),
            ("Log", 0.37, unchecked((long)0xBFEFD0EA24BF89B8L)),
            ("Log", 4.0, 0x3FF62E42FEFA39EFL),
            ("Log", 1e10, 0x4037069E2AA2AA5BL),
            ("Acos", -0.81, 0x40041E9D49EA60F2L),
            ("Acos", 0.3, 0x3FF441F5ECBEEF58L),
            ("Asin", 0.77, 0x3FEC1F777A9974F4L),
            ("Atan", 2.5, 0x3FF30B6D796A4DA8L),
            ("Atan", -0.2, unchecked((long)0xBFC94441F8F7260AL)),
        };
        foreach (var (fn, x, bits) in unary)
        {
            double got = fn switch
            {
                "Sin" => DetMath.Sin(x), "Cos" => DetMath.Cos(x), "Exp" => DetMath.Exp(x), "Log" => DetMath.Log(x),
                "Acos" => DetMath.Acos(x), "Asin" => DetMath.Asin(x), "Atan" => DetMath.Atan(x),
                _ => throw new InvalidOperationException(fn),
            };
            Assert.True(bits == BitConverter.DoubleToInt64Bits(got), $"{fn}({x}) = {got:R} (0x{BitConverter.DoubleToInt64Bits(got):X16})");
        }
        Assert.Equal(0x4007BC40CA9216FDL, BitConverter.DoubleToInt64Bits(DetMath.Atan2(0.3, -1.7)));
        Assert.Equal(0x3FE0B1ECD6401DB8L, BitConverter.DoubleToInt64Bits(DetMath.Pow(0.42, 0.75)));
    }
}
