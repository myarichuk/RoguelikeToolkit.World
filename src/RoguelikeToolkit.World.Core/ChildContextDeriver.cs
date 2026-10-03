using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Builds the <see cref="ParentContext"/> a region cell hands to its local map:
/// a least-squares plane over the cell and its hex ring, the strike re-projected
/// at the cell, the downhill aspect, and the up/down-stream ring neighbors.
/// </summary>
internal static class ChildContextDeriver
{
    public static ParentContext Derive(RegionHandle region, int cellIndex)
    {
        var parent = region.Parent;
        int size = region.Size;
        var (cq, cr) = OffsetGrid.FromIndex(cellIndex, size);

        Span<int> nb = stackalloc int[6];
        int adjacent = GridProjection.GetHexAdjacent(cellIndex, size, nb);

        var frame = new TangentFrame(region.Bounds.Center, region.Bounds.RadiusKm, 1e-9);
        var (ux0, vx0) = GridProjection.CellUV(cq, cr, size);
        var cell = region.Cells[cellIndex];

        // Sample points (cell first, then ring) in radians from the region center.
        var ringHeights = new float[adjacent];
        var pts = new (double E, double N, double H)[adjacent + 1];
        pts[0] = (ux0 * frame.Angle, vx0 * frame.Angle, cell.Elevation);
        double sumH = cell.Elevation;
        for (int k = 0; k < adjacent; k++)
        {
            var (nq, nr) = OffsetGrid.FromIndex(nb[k], size);
            var (nu, nv) = GridProjection.CellUV(nq, nr, size);
            float h = region.Cells[nb[k]].Elevation;
            pts[k + 1] = (nu * frame.Angle, nv * frame.Angle, h);
            ringHeights[k] = h;
            sumH += h;
        }
        double mean = sumH / (adjacent + 1);
        var gradient = FitGradient(pts, mean, frame, parent);

        // Child center: offset from the region center by the cell position.
        var childPos = frame.Offset(ux0, vx0);
        var childCenter = childPos.ToGeoCoord();
        var (eastC, northC) = GridProjection.EastNorth(childCenter, childPos);

        // Strike inherited from the region parent, re-projected at the child center.
        var strike = parent.OrogenyStrike - childPos * Vector3D.Dot(parent.OrogenyStrike, childPos);
        strike = strike.Length > 1e-9 ? strike.Normalize() : eastC;

        double aspect = 0;
        if (gradient.Length > 1e-9)
        {
            var downhill = gradient * -1;
            aspect = DetMath.Atan2(Vector3D.Dot(downhill, eastC), Vector3D.Dot(downhill, northC));
        }

        // Highest ring neighbor feeds the cell, lowest drains it (only if below it).
        int entry = -1, exit = -1;
        float maxH = float.NegativeInfinity, minH = float.PositiveInfinity;
        for (int k = 0; k < adjacent; k++)
        {
            if (ringHeights[k] > maxH) { maxH = ringHeights[k]; entry = nb[k]; }
            if (ringHeights[k] < minH) { minH = ringHeights[k]; exit = nb[k]; }
        }
        if (adjacent == 0 || minH >= cell.Elevation) exit = -1;

        return new ParentContext
        {
            MeanElevation = (float)mean,
            DominantBiome = cell.Biome,
            MeanTemperature = parent.MeanTemperature,
            MeanPrecipitation = parent.MeanPrecipitation,
            MeanMoisture = cell.Moisture,
            ElevationGradient = gradient,
            AspectRadians = aspect,
            OrogenyStrike = strike,
            NeighborHeights = ringHeights,
            FlowEntryTile = entry,
            FlowExitTile = exit,
            Wind = parent.Wind,
            IsRiver = cell.IsRiver,
            Flow = cell.Flow,
        };
    }

    /// <summary>
    /// Least-squares height gradient (east, north) over the samples, as a tangent
    /// vector at the region center. Degenerate sample sets fall back to the
    /// parent gradient.
    /// </summary>
    private static Vector3D FitGradient(
        (double E, double N, double H)[] pts, double mean, TangentFrame frame, ParentContext parent)
    {
        double sEE = 0, sEN = 0, sNN = 0, sE = 0, sN = 0;
        for (int k = 0; k < pts.Length; k++)
        {
            double dh = pts[k].H - mean;
            sEE += pts[k].E * pts[k].E; sEN += pts[k].E * pts[k].N; sNN += pts[k].N * pts[k].N;
            sE += dh * pts[k].E; sN += dh * pts[k].N;
        }
        double det = sEE * sNN - sEN * sEN;
        double gE, gN;
        if (Math.Abs(det) > 1e-12)
        {
            gE = (sE * sNN - sN * sEN) / det;
            gN = (sEE * sN - sEN * sE) / det;
        }
        else
        {
            gE = Vector3D.Dot(parent.ElevationGradient, frame.East);
            gN = Vector3D.Dot(parent.ElevationGradient, frame.North);
        }
        return frame.East * gE + frame.North * gN;
    }
}
