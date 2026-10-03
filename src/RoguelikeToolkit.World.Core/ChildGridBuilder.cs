using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Per-cell surface of one derived child map, before it is projected into
/// <see cref="RegionCell"/>s or <see cref="LocalTile"/>s.
/// </summary>
internal sealed class ChildGrid
{
    public int Size;
    public int Count;
    public float[] Heights = Array.Empty<float>();
    public double[] Jitter = Array.Empty<double>();
    public bool[] IsRiver = Array.Empty<bool>();
    public float[] WaterDepth = Array.Empty<float>();
    public float[] Flow = Array.Empty<float>();
    public int RiverEntry = -1;
    public int RiverExit = -1;

    // Planar hex-lattice position (x, y) and border flag per cell.
    public double[] X = Array.Empty<double>();
    public double[] Y = Array.Empty<double>();
    public bool[] IsEdge = Array.Empty<bool>();
}

/// <summary>
/// Derives the surface of a child map from its <see cref="ParentContext"/> in
/// three passes: terrain (parent plane + warped ridged noise, pinned at the
/// border), river threading, and local drainage/standing water.
/// </summary>
internal static class ChildGridBuilder
{
    private const double SeaLevel = 0.0;

    public static ChildGrid Build(
        ParentContext parent, GeoCoord center, double radiusKm,
        uint seed, int size, RegionDetailOptions options, int noiseSeed)
    {
        int count = size * size;
        var grid = new ChildGrid
        {
            Size = size,
            Count = count,
            Heights = new float[count],
            Jitter = new double[count],
            IsRiver = new bool[count],
            WaterDepth = new float[count],
            Flow = new float[count],
            X = new double[count],
            Y = new double[count],
            IsEdge = new bool[count],
        };

        var frame = new TangentFrame(center, radiusKm, 0.0);
        FillTerrain(grid, parent, frame, seed, options, noiseSeed);

        // River threading (parent river or high parent flow) else aspect drainage.
        double downhillX = Math.Sin(parent.AspectRadians);
        double downhillY = Math.Cos(parent.AspectRadians);
        if (parent.IsRiver || parent.Flow >= options.RiverFlowThreshold)
            ThreadRiver(grid, options, downhillX, downhillY);

        ApplyDrainage(grid, downhillX, downhillY);
        return grid;
    }

    /// <summary>Heights and moisture jitter: parent plane plus strike-warped noise, pinned toward the plane at the border.</summary>
    private static void FillTerrain(
        ChildGrid grid, ParentContext parent, TangentFrame frame,
        uint seed, RegionDetailOptions options, int noiseSeed)
    {
        int size = grid.Size;
        var (up, east, north, ang) = (frame.Up, frame.East, frame.North, frame.Angle);

        // Strike in the local east/north frame (unit, with contour fallback).
        double sU = Vector3D.Dot(parent.OrogenyStrike, east);
        double sV = Vector3D.Dot(parent.OrogenyStrike, north);
        double sLen = Math.Sqrt(sU * sU + sV * sV);
        if (sLen < 1e-9) { sU = 1; sV = 0; sLen = 1; }
        sU /= sLen; sV /= sLen;
        var strike = east * sU + north * sV;
        var perp = east * -sV + north * sU;

        double gU = Vector3D.Dot(parent.ElevationGradient, east);
        double gV = Vector3D.Dot(parent.ElevationGradient, north);

        double across = options.AcrossStrikeScale;
        double along = options.AlongStrikeScale;
        double freq = options.DetailFrequency;
        float amp = options.DetailAmplitude;
        double pinWidth = Math.Max(options.EdgePinCells, 1e-6);
        double kBase = 5.25 + (seed % 4096) * 0.00213;

        for (int i = 0; i < grid.Count; i++)
        {
            var (q, r) = OffsetGrid.FromIndex(i, size);
            var (u, v) = GridProjection.CellUV(q, r, size);
            grid.X[i] = 1.5 * q;
            grid.Y[i] = GridProjection.Sqrt3 * (r + q * 0.5);
            grid.IsEdge[i] = q == 0 || r == 0 || q == size - 1 || r == size - 1;

            double baseH = parent.MeanElevation + (gU * u + gV * v) * ang;

            // Anisotropic warp: compress across the strike, elongate along it.
            double s = (u * sU + v * sV) * freq;
            double c = (-u * sV + v * sU) * freq;
            var sample = up * kBase + strike * (s * along) + perp * (c * across);
            double detail = SphereNoise.RidgedFbm(sample, noiseSeed, options.NoiseOctaves, 2.1, options.DetailGain);
            grid.Jitter[i] = SphereNoise.Value(sample * 0.5 + up * 3.7, noiseSeed + 77);

            int edge = Math.Min(Math.Min(q, size - 1 - q), Math.Min(r, size - 1 - r));
            double t = Math.Clamp(edge / pinWidth, 0.0, 1.0);
            double pin = t * t * (3.0 - 2.0 * t);
            grid.Heights[i] = (float)(baseH + ((detail - 0.52) * 2.0) * amp * pin);
        }
    }

    /// <summary>Carves a least-cost channel from the upstream border cell to the downstream one.</summary>
    private static void ThreadRiver(ChildGrid grid, RegionDetailOptions options, double downhillX, double downhillY)
    {
        int entry = -1, exit = -1;
        double minD = double.PositiveInfinity, maxD = double.NegativeInfinity;
        for (int i = 0; i < grid.Count; i++)
        {
            if (!grid.IsEdge[i]) continue;
            double d = grid.X[i] * downhillX + grid.Y[i] * downhillY;
            if (d < minD - 1e-12 || (Math.Abs(d - minD) <= 1e-12 && (entry < 0 || i < entry))) { minD = d; entry = i; }
            if (d > maxD + 1e-12 || (Math.Abs(d - maxD) <= 1e-12 && (exit < 0 || i < exit))) { maxD = d; exit = i; }
        }
        if (entry < 0 || exit < 0) return;

        var path = FindRiverPath(grid.Heights, grid.Size, entry, exit);
        grid.RiverEntry = entry;
        grid.RiverExit = exit;
        for (int k = 0; k < path.Count; k++)
        {
            int cell = path[k];
            double profile = path.Count > 1
                ? Math.Pow(Math.Sin(Math.PI * (k + 0.5) / path.Count), 0.75)
                : 1.0;
            float depth = (float)(options.RiverCarveDepth * profile);
            grid.Heights[cell] -= depth;
            grid.IsRiver[cell] = true;
            grid.WaterDepth[cell] = Math.Max(grid.WaterDepth[cell], 0.03f + depth * 0.8f);
        }
    }

    /// <summary>Local discharge on the final surface (sinks spill along the aspect) and the standing water it implies.</summary>
    private static void ApplyDrainage(ChildGrid grid, double downhillX, double downhillY)
    {
        var flow = AccumulateFlow(grid.Heights, grid.Size, grid.X, grid.Y, downhillX, downhillY);
        for (int i = 0; i < grid.Count; i++)
        {
            grid.Flow[i] = flow[i];
            if (grid.Heights[i] < SeaLevel)
            {
                grid.WaterDepth[i] = Math.Max(grid.WaterDepth[i], (float)(SeaLevel - grid.Heights[i]));
            }
            else if (!grid.IsRiver[i] && flow[i] >= 5f)
            {
                grid.WaterDepth[i] = Math.Max(grid.WaterDepth[i], Math.Min(0.015f, 0.004f * (flow[i] - 4f)));
            }
        }
    }

    private static List<int> FindRiverPath(float[] heights, int size, int entry, int exit)
    {
        int n = heights.Length;
        var g = new double[n];
        var cameFrom = new int[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { g[i] = double.PositiveInfinity; cameFrom[i] = -1; }
        g[entry] = 0;
        var pq = new PriorityQueue<int, (double F, int Index)>();
        pq.Enqueue(entry, (GridProjection.HexDistance(entry, exit, size), entry));
        Span<int> nb = stackalloc int[6];
        while (pq.Count > 0)
        {
            int cur = pq.Dequeue();
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == exit) break;
            int m = GridProjection.GetHexAdjacent(cur, size, nb);
            for (int k = 0; k < m; k++)
            {
                int j = nb[k];
                if (closed[j]) continue;
                double step = 1.0 + Math.Max(0.0, heights[j] - heights[cur]) * 12.0;
                double ng = g[cur] + step;
                if (ng < g[j] - 1e-12)
                {
                    g[j] = ng;
                    cameFrom[j] = cur;
                    pq.Enqueue(j, (ng + GridProjection.HexDistance(j, exit, size), j));
                }
            }
        }
        var path = new List<int>();
        if (!closed[exit]) { path.Add(entry); return path; }
        for (int c = exit; c >= 0; c = cameFrom[c])
        {
            path.Add(c);
            if (c == entry) break;
        }
        path.Reverse();
        return path;
    }

    private static float[] AccumulateFlow(
        float[] heights, int size, double[] xs, double[] ys, double downhillX, double downhillY)
    {
        int n = heights.Length;
        var flow = new float[n];
        for (int i = 0; i < n; i++) flow[i] = 1f;
        var order = TileOrdering.DescendingByValue(heights);
        Span<int> nb = stackalloc int[6];
        foreach (int i in order)
        {
            int m = GridProjection.GetHexAdjacent(i, size, nb);
            int best = -1;
            float bestH = heights[i];
            for (int k = 0; k < m; k++)
            {
                int j = nb[k];
                if (heights[j] < bestH || (best >= 0 && heights[j] == bestH && j < best))
                { bestH = heights[j]; best = j; }
            }
            if (best < 0 && m > 0)
            {
                // Flat/sink spill along the inherited aspect.
                var (iq, ir) = OffsetGrid.FromIndex(i, size);
                double align = double.NegativeInfinity;
                for (int k = 0; k < m; k++)
                {
                    int j = nb[k];
                    var (jq, jr) = OffsetGrid.FromIndex(j, size);
                    double dx = 1.5 * (jq - iq);
                    double dy = GridProjection.Sqrt3 * ((jq - iq) * 0.5 + (jr - ir));
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    double a = len > 1e-12 ? (dx * downhillX + dy * downhillY) / len : -2.0;
                    if (a > align + 1e-12 || (Math.Abs(a - align) <= 1e-12 && (best < 0 || heights[j] < heights[best])))
                    { align = a; best = j; }
                }
            }
            if (best >= 0) flow[best] += flow[i];
        }
        return flow;
    }
}
