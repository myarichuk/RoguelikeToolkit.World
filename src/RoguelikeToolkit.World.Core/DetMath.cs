using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Transcendental functions built only from IEEE-754 operations whose results
/// are exactly specified (+, -, *, /, <see cref="Math.Sqrt(double)"/>,
/// <see cref="Math.Floor(double)"/>, <see cref="Math.ScaleB(double, int)"/>), so
/// the same inputs give the same bits on every OS, CPU and runtime build.
/// <c>Math.Sin/Cos/Exp/Acos/...</c> defer to the platform C library, whose last
/// bit differs between libm implementations; a world seed must not depend on
/// where it was generated. The RyuJIT does not contract <c>a*b+c</c> into FMA,
/// which this relies on.
/// </summary>
/// <remarks>
/// Algorithms and coefficients follow fdlibm (kernel sin/cos, exp, log) with
/// Cody-Waite argument reduction; accuracy is within ~1 ulp of libm over the
/// ranges world generation uses (pinned by tests). Arguments of huge magnitude
/// (|x| &gt; 1e5 rad for sin/cos) reduce with reduced precision but stay deterministic.
/// </remarks>
public static class DetMath
{
    private const double PiOver2 = 1.5707963267948966;
    private const double InvPiOver2 = 6.36619772367581382433e-01;
    // pi/2 split into pieces whose products with a small integer are exact.
    private const double Pio2_1 = 1.57079632673412561417e+00;
    private const double Pio2_1t = 6.07710050650619224932e-11;
    private const double Pio2_2 = 6.07710050630396597660e-11;
    private const double Pio2_2t = 2.02226624879595063154e-21;
    private const double Pio2_3 = 2.02226624871116645580e-21;
    private const double Pio2_3t = 8.47842766036889956997e-32;

    public static double Sin(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
        int q = Reduce(x, out double r);
        return (q & 3) switch
        {
            0 => KernelSin(r),
            1 => KernelCos(r),
            2 => -KernelSin(r),
            _ => -KernelCos(r),
        };
    }

    public static double Cos(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
        int q = Reduce(x, out double r);
        return (q & 3) switch
        {
            0 => KernelCos(r),
            1 => -KernelSin(r),
            2 => -KernelCos(r),
            _ => KernelSin(r),
        };
    }

    /// <summary>x = q * pi/2 + r with |r| &lt;= ~pi/4; returns q.</summary>
    private static int Reduce(double x, out double r)
    {
        double ax = Math.Abs(x);
        if (ax <= 0.7853981633974483) { r = x; return 0; }
        double k = Math.Floor(x * InvPiOver2 + 0.5);
        if (ax > 1e5)
        {
            // Beyond the exact-product range of the split constants: fold by whole turns first.
            double turns = Math.Floor(x / (4 * PiOver2));
            x -= turns * (4 * PiOver2);
            k = Math.Floor(x * InvPiOver2 + 0.5);
        }
        double t = x - k * Pio2_1;
        t -= k * Pio2_2;
        t -= k * Pio2_3;
        t -= k * Pio2_3t;
        r = t;
        long qi = (long)k;
        return (int)(qi & 3);
    }

    private static double KernelSin(double x)
    {
        const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03,
            S3 = -1.98412698298579493134e-04, S4 = 2.75573137070700676789e-06,
            S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
        double z = x * x;
        double v = z * x;
        double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
        return x + v * (S1 + z * r);
    }

    private static double KernelCos(double x)
    {
        const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03,
            C3 = 2.48015872894767294178e-05, C4 = -2.75573143513906633035e-07,
            C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
        double z = x * x;
        double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        double hz = 0.5 * z;
        double w = 1.0 - hz;
        return w + (((1.0 - w) - hz) + z * r);
    }

    public static double Exp(double x)
    {
        const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10,
            InvLn2 = 1.44269504088896338700e+00,
            P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03,
            P3 = 6.61375632143793436117e-05, P4 = -1.65339022054652515390e-06,
            P5 = 4.13813679705723846039e-08;
        if (double.IsNaN(x)) return double.NaN;
        if (x > 709.782712893384) return double.PositiveInfinity;
        if (x < -745.1332191019411) return 0.0;
        if (Math.Abs(x) < 1.0 / (1 << 28)) return 1.0 + x;

        double kd = Math.Floor(x * InvLn2 + 0.5);
        double hi = x - kd * Ln2Hi;
        double lo = kd * Ln2Lo;
        double r = hi - lo;
        double t = r * r;
        double c = r - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        double y = 1.0 - ((lo - (r * c) / (2.0 - c)) - hi);
        return Math.ScaleB(y, (int)kd);
    }

    public static double Log(double x)
    {
        const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10,
            Lg1 = 6.666666666666735130e-01, Lg2 = 3.999999999940941908e-01,
            Lg3 = 2.857142874366239149e-01, Lg4 = 2.222219843214978396e-01,
            Lg5 = 1.818357216161805012e-01, Lg6 = 1.531383769920937332e-01,
            Lg7 = 1.479819860511658591e-01;
        if (double.IsNaN(x) || x < 0) return double.NaN;
        if (x == 0) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return x;

        // x = m * 2^e with m in [sqrt(2)/2, sqrt(2)).
        int e = 0;
        if (x < double.Epsilon * 4503599627370496.0) { x *= 18014398509481984.0; e -= 54; }
        long bits = BitConverter.DoubleToInt64Bits(x);
        e += (int)((bits >> 52) & 0x7FF) - 1023;
        double m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L);
        if (m > 1.4142135623730951) { m *= 0.5; e++; }

        double f = m - 1.0;
        double s = f / (2.0 + f);
        double z = s * s;
        double w = z * z;
        double t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
        double t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
        double rr = t2 + t1;
        double hfsq = 0.5 * f * f;
        double dk = e;
        return dk * Ln2Hi - ((hfsq - (s * (hfsq + rr) + dk * Ln2Lo)) - f);
    }

    public static double Pow(double x, double y)
    {
        if (y == 0.0) return 1.0;
        if (x == 1.0) return 1.0;
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (x == 0.0)
            return y > 0 ? 0.0 : double.PositiveInfinity;
        if (x < 0)
        {
            if (y != Math.Floor(y) || double.IsInfinity(y)) return double.NaN;
            double mag = Exp(y * Log(-x));
            return Math.Abs(y % 2.0) == 1.0 ? -mag : mag;
        }
        return Exp(y * Log(x));
    }

    /// <summary>Arctangent in [-pi/2, pi/2]: two half-angle reductions, then a Taylor series on |t| &lt;= tan(pi/16).</summary>
    public static double Atan(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        double ax = Math.Abs(x);
        if (double.IsPositiveInfinity(ax)) return x > 0 ? PiOver2 : -PiOver2;

        bool inverted = ax > 1.0;
        double t = inverted ? 1.0 / ax : ax;
        t = t / (1.0 + Math.Sqrt(1.0 + t * t));
        t = t / (1.0 + Math.Sqrt(1.0 + t * t));
        double t2 = t * t;
        double p = 1.0 / 25.0;
        p = 1.0 / 23.0 - t2 * p; p = 1.0 / 21.0 - t2 * p; p = 1.0 / 19.0 - t2 * p;
        p = 1.0 / 17.0 - t2 * p; p = 1.0 / 15.0 - t2 * p; p = 1.0 / 13.0 - t2 * p;
        p = 1.0 / 11.0 - t2 * p; p = 1.0 / 9.0 - t2 * p; p = 1.0 / 7.0 - t2 * p;
        p = 1.0 / 5.0 - t2 * p; p = 1.0 / 3.0 - t2 * p; p = 1.0 - t2 * p;
        double a = 4.0 * (t * p);
        if (inverted) a = PiOver2 - a;
        return x < 0 ? -a : a;
    }

    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (x == 0.0 && y == 0.0)
            return double.IsNegative(x) ? (double.IsNegative(y) ? -Math.PI : Math.PI) : (double.IsNegative(y) ? -0.0 : 0.0);
        if (double.IsInfinity(x) || double.IsInfinity(y))
        {
            if (double.IsInfinity(x) && double.IsInfinity(y))
            {
                double quarter = x > 0 ? Math.PI / 4 : 3 * Math.PI / 4;
                return y > 0 ? quarter : -quarter;
            }
            if (double.IsInfinity(y)) return y > 0 ? PiOver2 : -PiOver2;
            return x > 0 ? (double.IsNegative(y) ? -0.0 : 0.0) : (double.IsNegative(y) ? -Math.PI : Math.PI);
        }

        double ay = Math.Abs(y), ax = Math.Abs(x);
        // Fold into the first octant for accuracy, then unfold.
        double a = ay > ax ? PiOver2 - Atan(ax / ay) : Atan(ay / ax);
        if (x < 0 || (x == 0 && double.IsNegative(x))) a = Math.PI - a;
        return y < 0 || (y == 0 && double.IsNegative(y)) ? -a : a;
    }

    public static double Acos(double x)
    {
        if (double.IsNaN(x) || x > 1.0 || x < -1.0) return double.NaN;
        return Atan2(Math.Sqrt((1.0 - x) * (1.0 + x)), x);
    }

    public static double Asin(double x)
    {
        if (double.IsNaN(x) || x > 1.0 || x < -1.0) return double.NaN;
        return Atan2(x, Math.Sqrt((1.0 - x) * (1.0 + x)));
    }
}
