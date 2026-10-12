using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>The solved surface circulation: stream function, velocity and the heat the currents carry.</summary>
internal sealed class OceanCurrentField
{
    /// <summary>Stream function per tile (0 on land and so on every coast: no flow through a shore).</summary>
    public double[] Psi = Array.Empty<double>();

    /// <summary>
    /// Surface current per tile, tangent to the sphere, in units of <see cref="ReferenceSpeed"/>
    /// (1 = a strong boundary current; zero on land).
    /// </summary>
    public Vector3D[] Velocity = Array.Empty<Vector3D>();

    /// <summary>
    /// Sea-surface temperature minus the latitude's radiative equilibrium, in climate temperature units, per
    /// ocean tile: positive where a current carries tropical water poleward, negative where it brings polar
    /// water to the equator. Zero on land.
    /// </summary>
    public double[] Anomaly = Array.Empty<double>();

    /// <summary>The speed (stream-function units per radian) that <see cref="Velocity"/> is normalised by.</summary>
    public double ReferenceSpeed;
}

/// <summary>Tunables of the current solver; the defaults are the calibrated ones.</summary>
internal readonly struct OceanCurrentSettings
{
    /// <summary>
    /// Screening length of the stream function, in radians of arc: friction confines each gyre's
    /// influence to about this far. 0.4 rad is ~2500 km, the width of a basin-scale boundary current system.
    /// </summary>
    public double ScreeningLength { get; init; }

    /// <summary>
    /// Width of the western boundary layer at the equator, in radians of arc (it widens with latitude, as beta
    /// falls with cos latitude): the Stommel scale r / beta, a property of the planet and not of the mesh, so the
    /// same world has the same current at every icosphere size. Below about two tile spacings the layer is
    /// not resolved and the scheme saturates at one tile (the monotone limit), so coarse worlds see a wider, weaker jet.
    /// </summary>
    public double BoundaryWidth { get; init; }

    /// <summary>
    /// E-folding distance, in radians of arc, over which a current keeps the temperature it carried
    /// before the surrounding sea drags it back to its local equilibrium, at the reference speed.
    /// </summary>
    public double Reach { get; init; }

    /// <summary>
    /// When > 0, skip the multilevel solve and run this many flat Gauss-Seidel sweeps on the finest mesh: the slow,
    /// obviously-correct reference the hierarchical solver is tested against.
    /// </summary>
    public int ReferenceSweeps { get; init; }

    public static OceanCurrentSettings Default => new OceanCurrentSettings
    {
        ScreeningLength = 0.4,
        BoundaryWidth = 0.06,
        Reach = 0.45,
    };
}

/// <summary>
/// Surface ocean currents and the heat they carry, from the prevailing wind alone.
/// <para>
/// The wind stress is the wind band structure the climate stage already uses (easterly trades, westerlies,
/// polar easterlies, cells of 30 degrees times the planet's band scale) as a smooth profile
/// <c>tau = -sin(k phi)</c>; its curl is what spins the gyres: anticyclonic at the subtropical highs
/// (about 30 degrees, where the stress curl is most negative), cyclonic at the subpolar lows (60). The
/// stream function solves the Stommel balance <c>r laplacian(psi) + beta d(psi)/dx - psi / l^2 = curl(tau)</c>
/// on the tile graph, with psi = 0 on every land tile. The beta term (the planet's rotation varying with
/// latitude) makes the solution asymmetric: the interior drifts equatorward while a thin, fast boundary
/// current runs poleward on the *western* side of each basin, which is why the Gulf Stream and the
/// Kuroshio are narrow and swift while the Canary and Humboldt currents are broad and slow.
/// </para>
/// <para>
/// Heat: a parcel carried along the flow keeps the temperature of where it came from and relaxes to the
/// local equilibrium with an e-folding distance that grows with the current's speed. The steady anomaly
/// field is the fixed point of that relaxation over the upstream neighbours, which is a contraction, so a
/// fixed number of Jacobi sweeps reaches it. Every iteration count is a pure function of the tile
/// count, and the stream function is solved in index order, so the field is identical for the same
/// world on every platform.
/// </para>
/// </summary>
internal static class OceanCurrents
{
    // Gauss-Seidel (omega = 1) on purpose. The operator is a diagonally dominant M-matrix, for which plain
    // Gauss-Seidel provably converges whatever the ocean's shape; over-relaxation does not carry that guarantee for
    // a non-symmetric operator and was measured to diverge (omega 1.6) on ordinary worlds.
    private const int CoarsestLevel = 3;      // 642 tiles: solved to convergence
    private const int CoarseSweeps = 600;
    // Sweeps per screening length, per level: after prolongation what is left is coastline detail, whose
    // relaxation takes a number of sweeps proportional to l / h. Measured against a 20000-sweep flat solve: ~10 leaves
    // psi at 1e-3 relative L2 and the temperature anomaly's mean error at 1e-4 on sizes 4-6.
    private const double LevelSweepsPerScreen = 10.0;
    // The anomaly relaxation is a contraction (exp(-h / reach) per sweep); in place, 2 sweeps per reach leave 1e-5.
    private const double HeatSweepsPerReach = 2.0;
    private const double SpeedFloor = 0.05;
    private const double SpeedCeiling = 2.5;

    /// <summary>
    /// Wind-stress curl at a latitude (sine given), dimensionless, positive = cyclonic in the northern
    /// hemisphere and antisymmetric across the equator. <paramref name="bandScale"/> stretches the cells like
    /// the climate stage does.
    /// </summary>
    internal static double WindStressCurl(double sinLatitude, double bandScale)
    {
        double a = Math.Abs(sinLatitude);
        double cosLat = Math.Sqrt(Math.Max(0.0, 1.0 - a * a));
        double phi = DetMath.Asin(Math.Min(1.0, a));
        double k = 180.0 / (30.0 * bandScale);
        double curl = k * DetMath.Cos(k * phi) - DetMath.Sin(k * phi) * a / Math.Max(cosLat, 0.05);
        // Rotation (and with it any steady response) fades at the equator, and the grid pinches at the pole.
        curl *= Smooth(a / 0.2) * Smooth(cosLat / 0.17);
        return sinLatitude < 0.0 ? -curl : curl;
    }

    private static double Sum(double[] coef, int i, int count)
    {
        double sum = 0.0;
        for (int k = 0; k < count; k++) sum += coef[i * 6 + k];
        return sum;
    }

    private static double Smooth(double t)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>One resolution of the tile graph: the first <see cref="N"/> tiles of the finer mesh, with their own adjacency.</summary>
    private sealed class Level
    {
        public int N;
        public int[] Nb = Array.Empty<int>();      // stride 6
        public byte[] Cnt = Array.Empty<byte>();
        public double[] Chord = Array.Empty<double>(); // stride 6
    }

    /// <summary>Icosphere recursion level whose vertex count is <paramref name="tiles"/> (10 * 4^k + 2), or -1.</summary>
    private static int IcosphereLevel(int tiles)
    {
        for (int k = 0, n = 12; k < 14; k++, n = (n - 2) * 4 + 2)
        {
            if (n == tiles) return k;
            if (n > tiles) break;
        }
        return -1;
    }

    /// <summary>
    /// The mesh one level up the hierarchy: an icosphere appends each level's midpoint vertices after the previous
    /// level's, so the first tiles of a fine mesh are the coarse mesh, and every midpoint has exactly two
    /// neighbours below it, the ends of the coarse edge it bisects.
    /// </summary>
    private static Level Coarsen(Level fine, int coarseCount, ReadOnlySpan<Vector3D> vectors)
    {
        var coarse = new Level { N = coarseCount, Nb = new int[coarseCount * 6], Cnt = new byte[coarseCount], Chord = new double[coarseCount * 6] };
        for (int m = coarseCount; m < fine.N; m++)
        {
            int a = -1, b = -1;
            for (int k = 0; k < fine.Cnt[m]; k++)
            {
                int j = fine.Nb[m * 6 + k];
                if (j >= coarseCount) continue;
                if (a < 0) a = j; else b = j;
            }
            if (a < 0 || b < 0) continue;
            coarse.Nb[a * 6 + coarse.Cnt[a]++] = b;
            coarse.Nb[b * 6 + coarse.Cnt[b]++] = a;
        }
        for (int i = 0; i < coarseCount; i++)
            for (int k = 0; k < coarse.Cnt[i]; k++)
                coarse.Chord[i * 6 + k] = (vectors[coarse.Nb[i * 6 + k]] - vectors[i]).Length;
        return coarse;
    }

    /// <summary>
    /// Gauss-Seidel sweeps of the screened Stommel operator on one level, with land (psi = 0) wherever
    /// <paramref name="ocean"/> is false. The coefficients are rebuilt per level from that level's own geometry.
    /// </summary>
    private static void Relax(Level lv, bool[] ocean, ReadOnlySpan<Vector3D> vectors, double bandScale, OceanCurrentSettings settings,
        double[] psi, int sweeps, double sweepsPerScreen = 0.0)
    {
        int n = lv.N;
        var coef = new double[n * 6];
        var diag = new double[n];
        var rhs = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!ocean[i]) continue;
            var v = vectors[i];
            double horiz = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            var east = horiz > 1e-9 ? new Vector3D(-v.Y / horiz, v.X / horiz, 0.0) : new Vector3D(1, 0, 0);
            double cosLat = Math.Sqrt(Math.Max(0.0, 1.0 - v.Z * v.Z));
            int ni = lv.Cnt[i];
            double meanChord = 0.0;
            for (int k = 0; k < ni; k++) meanChord += lv.Chord[i * 6 + k];
            meanChord /= Math.Max(1, ni);
            // gamma = beta h / r = cos(lat) h / delta, kept below 2 so every coefficient stays positive (monotone scheme).
            double gamma = Math.Min(1.95, cosLat * meanChord / settings.BoundaryWidth);
            double sum = 0.0;
            for (int k = 0; k < ni; k++)
            {
                int j = lv.Nb[i * 6 + k];
                double c = lv.Chord[i * 6 + k];
                double eDot = c > 0.0 ? Vector3D.Dot(east, (vectors[j] - v) * (1.0 / c)) : 0.0;
                double a = (1.0 + 0.5 * gamma * eDot) / ni;
                coef[i * 6 + k] = a;
                sum += a;
            }
            double sigma = meanChord * meanChord / (4.0 * settings.ScreeningLength * settings.ScreeningLength);
            diag[i] = sum + sigma;
            rhs[i] = 0.25 * meanChord * meanChord * WindStressCurl(v.Z, bandScale);
        }
        if (sweepsPerScreen > 0.0)
        {
            double chordSum = 0.0; long chordN = 0;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < lv.Cnt[i]; k++) { chordSum += lv.Chord[i * 6 + k]; chordN++; }
            sweeps = (int)Math.Clamp(Math.Ceiling(sweepsPerScreen * settings.ScreeningLength / (chordSum / Math.Max(1L, chordN))), 20, 5000);
        }
        for (int s = 0; s < sweeps; s++)
        {
            for (int i = 0; i < n; i++)
            {
                if (!ocean[i]) continue;
                double acc = 0.0;
                int ni = lv.Cnt[i];
                for (int k = 0; k < ni; k++) acc += coef[i * 6 + k] * psi[lv.Nb[i * 6 + k]]; // land psi stays 0
                psi[i] = (acc - rhs[i]) / diag[i];
            }
        }
    }

    /// <summary>Linear interpolation to the next level: a midpoint takes the mean of the two tiles of the edge it bisects.</summary>
    private static void Prolong(Level fine, int coarseCount, double[] psi)
    {
        for (int m = coarseCount; m < fine.N; m++)
        {
            double sum = 0.0; int found = 0;
            for (int k = 0; k < fine.Cnt[m]; k++)
            {
                int j = fine.Nb[m * 6 + k];
                if (j < coarseCount) { sum += psi[j]; found++; }
            }
            psi[m] = found > 0 ? sum / found : 0.0;
        }
    }

    public static OceanCurrentField Solve(WorldDataStore store, ReadOnlySpan<ElevationInfo> elev, ReadOnlySpan<Vector3D> vectors,
        PlanetParameters planet, OceanCurrentSettings settings)
    {
        int n = store.TileCount;
        var field = new OceanCurrentField
        {
            Psi = new double[n],
            Velocity = new Vector3D[n],
            Anomaly = new double[n],
        };

        var ocean = new bool[n];
        int oceanCount = 0;
        for (int i = 0; i < n; i++)
        {
            ocean[i] = elev[i].Height < ElevationGenerationStage.SeaLevel;
            if (ocean[i]) oceanCount++;
        }
        if (oceanCount == 0) return field;

        // Adjacency with chord lengths of the finest level (the world itself), once.
        var fine = new Level { N = n, Nb = new int[n * 6], Cnt = new byte[n], Chord = new double[n * 6] };
        var nb = fine.Nb;
        var cnt = fine.Cnt;
        var chord = fine.Chord;
        Span<int> tmp = stackalloc int[6];
        double chordSum = 0.0;
        long chordN = 0;
        for (int i = 0; i < n; i++)
        {
            int a = store.GetAdjacent(i, tmp);
            cnt[i] = (byte)a;
            for (int k = 0; k < a; k++)
            {
                nb[i * 6 + k] = tmp[k];
                double c = (vectors[tmp[k]] - vectors[i]).Length;
                chord[i * 6 + k] = c;
                chordSum += c;
                chordN++;
            }
        }
        double h = chordSum / Math.Max(1L, chordN);
        double bandScale = planet.BandScale;

        // Stream function. Gauss-Seidel needs O((l/h)^2) sweeps on one mesh, which is hopeless on a fine world, but an
        // icosphere's levels nest, so solve the coarse problem first and refine: the coarse answer carries the
        // basin-scale circulation and each finer level only has the coastline detail left to relax.
        var psi = field.Psi;
        int top = IcosphereLevel(n);
        if (settings.ReferenceSweeps > 0 || top < 0 || top <= CoarsestLevel)
        {
            // Not an icosphere, or already coarse: one mesh, many sweeps (also the reference for the hierarchical solver).
            int flat = settings.ReferenceSweeps > 0 ? settings.ReferenceSweeps : (top < 0 ? 4000 : CoarseSweeps);
            Relax(fine, ocean, vectors, bandScale, settings, psi, flat);
        }
        else
        {
            var levels = new Level[top + 1];
            levels[top] = fine;
            int[] counts = new int[top + 1];
            counts[top] = n;
            for (int k = top - 1; k >= CoarsestLevel; k--)
            {
                counts[k] = (counts[k + 1] - 2) / 4 + 2;
                levels[k] = Coarsen(levels[k + 1], counts[k], vectors);
            }
            for (int k = CoarsestLevel; k <= top; k++)
            {
                if (k > CoarsestLevel) Prolong(levels[k], counts[k - 1], psi);
                // Land is psi = 0 whatever the interpolation said.
                for (int i = 0; i < counts[k]; i++) if (!ocean[i]) psi[i] = 0.0;
                if (k == CoarsestLevel) Relax(levels[k], ocean, vectors, bandScale, settings, psi, CoarseSweeps);
                else Relax(levels[k], ocean, vectors, bandScale, settings, psi, 0, LevelSweepsPerScreen);
            }
        }
        // Velocity = radial x grad(psi); grad from the weighted neighbour differences.
        var speed = new double[oceanCount];
        int sp = 0;
        var vel = field.Velocity;
        for (int i = 0; i < n; i++)
        {
            if (!ocean[i]) continue;
            var v = vectors[i];
            int ni = cnt[i];
            double gx = 0, gy = 0, gz = 0;
            for (int k = 0; k < ni; k++)
            {
                int j = nb[i * 6 + k];
                double c = chord[i * 6 + k];
                if (c <= 0.0) continue;
                var d = (vectors[j] - v) * (1.0 / c);
                double w = (psi[j] - psi[i]) / c;
                gx += w * d.X; gy += w * d.Y; gz += w * d.Z;
            }
            double scale = 2.0 / Math.Max(1, ni);
            var grad = new Vector3D(gx * scale, gy * scale, gz * scale);
            var radial = v.Normalize();
            grad -= radial * Vector3D.Dot(grad, radial);
            var u = Vector3D.Cross(radial, grad);
            vel[i] = u;
            speed[sp++] = u.Length;
        }
        Array.Sort(speed);
        double reference = speed[Math.Min(speed.Length - 1, (int)(speed.Length * 0.75))];
        if (reference <= 0.0) reference = speed[speed.Length - 1]; // a sea too small to move most of its tiles
        field.ReferenceSpeed = reference;
        if (reference <= 0.0) return field;
        for (int i = 0; i < n; i++)
            if (ocean[i]) vel[i] = vel[i] * (1.0 / reference);
        // Heat carried along the flow: a contraction over the upstream neighbours.
        var teq = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!ocean[i]) continue;
            double absLat = Math.Abs(vectors[i].ToGeoCoord().Latitude);
            teq[i] = ClimateModel.Temperature(vectors[i], absLat, 0.0, 0.0, planet);
        }
        // Upstream weights: they depend only on the flow, so they are built once and the sweeps are a sparse product.
        var hc = new double[n * 6];
        var hsrc = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!ocean[i]) continue;
            double speedHere = vel[i].Length;
            if (speedHere < 1e-6) continue;
            var dir = vel[i] * (1.0 / speedHere);
            double reach = settings.Reach * Math.Clamp(speedHere, SpeedFloor, SpeedCeiling);
            double wsum = 0.0;
            int ni = cnt[i];
            for (int k = 0; k < ni; k++)
            {
                int j = nb[i * 6 + k];
                double c = chord[i * 6 + k];
                if (!ocean[j] || c <= 0.0) continue;
                double w = Vector3D.Dot(dir, (vectors[i] - vectors[j]) * (1.0 / c)); // > 0: j is upstream of i
                if (w <= 0.0) continue;
                hc[i * 6 + k] = w;
                wsum += w;
            }
            if (wsum <= 0.0) continue;
            for (int k = 0; k < ni; k++)
            {
                double w = hc[i * 6 + k];
                if (w <= 0.0) continue;
                int j = nb[i * 6 + k];
                double c = hc[i * 6 + k] = w / wsum * DetMath.Exp(-chord[i * 6 + k] / reach);
                hsrc[i] += c * (teq[j] - teq[i]);
            }
        }
        var anomaly = field.Anomaly;
        int heatSweeps = (int)Math.Clamp(Math.Ceiling(SpeedCeiling * HeatSweepsPerReach * settings.Reach / h), 40, 4000);
        for (int s = 0; s < heatSweeps; s++)
        {
            for (int i = 0; i < n; i++)
            {
                if (!ocean[i]) continue;
                double acc = hsrc[i];
                int ni = cnt[i];
                for (int k = 0; k < ni; k++)
                {
                    double c = hc[i * 6 + k];
                    if (c != 0.0) acc += c * anomaly[nb[i * 6 + k]];
                }
                anomaly[i] = acc;
            }
        }
        return field;
    }
}
