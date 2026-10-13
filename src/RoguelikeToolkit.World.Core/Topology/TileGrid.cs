using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>
/// The immutable geometry of one planet size: tile centers, unit vectors, CSR adjacency and a
/// coarse lat/lon bucket index that makes nearest-tile lookup sub-linear. One instance is built
/// per size and shared process-wide (bounded cache); stores hold it by reference.
/// </summary>
internal sealed class TileGrid
{
    public required GeoCoord[] Centers { get; init; }
    public required Vector3D[] TileVectors { get; init; }
    public required int[] NeighborOffsets { get; init; }
    public required int[] Neighbors { get; init; }
    // Coarse lat/lon bucket index accelerating <see cref="NearestTile"/>. Longitude search
    // radius widens toward the poles where meridians converge (see <see cref="NearestTile"/>).
    public required int LatCells { get; init; }
    public required int LonCells { get; init; }
    public required int LatRadiusCells { get; init; }
    public required double CoverDeg { get; init; }
    public required double LonCellDeg { get; init; }
    public required int[] CellOffsets { get; init; }
    public required int[] CellTiles { get; init; }

    private static readonly object TopologyLock = new();
    private static readonly Dictionary<int, TileGrid> TopologiesBySize = new();

    /// <summary>The shared grid for <paramref name="size"/>, built on first use.</summary>
    public static TileGrid For(int size)
    {
        lock (TopologyLock)
        {
            if (TopologiesBySize.TryGetValue(size, out var existing))
                return existing;

            var created = Create(size);
            // Bounded cache: live stores hold their topology by reference, so clearing
            // only drops cached entries, never memory in use.
            if (TopologiesBySize.Count >= 16)
                TopologiesBySize.Clear();
            TopologiesBySize[size] = created;
            return created;
        }
    }

    /// <summary>Copies the neighbors of <paramref name="index"/> into <paramref name="neighbors"/>; returns the count written.</summary>
    public int Adjacent(int index, Span<int> neighbors)
    {
        int start = NeighborOffsets[index];
        int end = NeighborOffsets[index + 1];
        int count = 0;

        for (int i = start; i < end && count < neighbors.Length; i++)
            neighbors[count++] = Neighbors[i];

        return count;
    }

    private static TileGrid Create(int recursionLevel)
    {
        IcosphereGenerator.Generate(recursionLevel, out Vector3D[] vertices, out TriangleIndices[] faces);

        int tileCount = vertices.Length;

        // Build adjacency from faces
        var adjacency = new HashSet<int>[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            adjacency[i] = new HashSet<int>();
        }

        foreach (var face in faces)
        {
            adjacency[face.v1].Add(face.v2);
            adjacency[face.v1].Add(face.v3);

            adjacency[face.v2].Add(face.v1);
            adjacency[face.v2].Add(face.v3);

            adjacency[face.v3].Add(face.v1);
            adjacency[face.v3].Add(face.v2);
        }

        var offsets = new int[tileCount + 1];
        int totalNeighbors = 0;
        for (int i = 0; i < tileCount; i++)
        {
            offsets[i] = totalNeighbors;
            totalNeighbors += adjacency[i].Count;
        }
        offsets[tileCount] = totalNeighbors;

        var neighbors = new int[totalNeighbors];
        int write = 0;
        for (int i = 0; i < tileCount; i++)
        {
            foreach (var neighbor in adjacency[i].OrderBy(n => n))
                neighbors[write++] = neighbor;
        }

        var centers = new GeoCoord[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            centers[i] = vertices[i].ToGeoCoord();
        }

        // Coarse lat/lon bucket index. Cells are at most half the minimum neighbor
        // spacing, so a bounded neighborhood around the query cell always contains
        // the nearest center for these near-regular grids.
        double minSpacing = double.MaxValue;
        double maxSpacing = 0.0;
        for (int i = 0; i < tileCount; i++)
        {
            for (int k = offsets[i]; k < offsets[i + 1]; k++)
            {
                double ang = DetMath.Acos(Math.Clamp(Vector3D.Dot(vertices[i], vertices[neighbors[k]]), -1.0, 1.0));
                if (ang < minSpacing) minSpacing = ang;
                if (ang > maxSpacing) maxSpacing = ang;
            }
        }

        double cellRad = Math.Max(minSpacing * 0.5, 1e-6);
        double cellDeg = cellRad * (180.0 / Math.PI);
        int latCells = (int)Math.Clamp(Math.Ceiling(180.0 / cellDeg), 4, 180);
        int lonCells = (int)Math.Clamp(Math.Ceiling(360.0 / cellDeg), 4, 360);
        // Covering bound: any query's nearest center lies within maxSpacing of it
        // (a query sits in some triangle; its nearest vertex is at most the longest
        // edge away, and every triangle edge is a neighbor pair).
        double coverDeg = maxSpacing * (180.0 / Math.PI);
        int latRadius = (int)Math.Clamp(Math.Ceiling(coverDeg / (180.0 / latCells)) + 1, 2, 8);

        int cellCount = latCells * lonCells;
        var cellSizes = new int[cellCount];
        var tileCell = new int[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            int cell = CellOf(centers[i], latCells, lonCells);
            tileCell[i] = cell;
            cellSizes[cell]++;
        }

        var cellOffsets = new int[cellCount + 1];
        for (int c = 0; c < cellCount; c++)
            cellOffsets[c + 1] = cellOffsets[c] + cellSizes[c];

        var cellTiles = new int[tileCount];
        var cursors = new int[cellCount];
        Array.Copy(cellOffsets, cursors, cellCount);
        for (int i = 0; i < tileCount; i++)
            cellTiles[cursors[tileCell[i]]++] = i;

        return new TileGrid
        {
            Centers = centers,
            TileVectors = vertices,
            NeighborOffsets = offsets,
            Neighbors = neighbors,
            LatCells = latCells,
            LonCells = lonCells,
            LatRadiusCells = latRadius,
            CoverDeg = coverDeg,
            LonCellDeg = 360.0 / lonCells,
            CellOffsets = cellOffsets,
            CellTiles = cellTiles
        };
    }

    private static int CellOf(GeoCoord coord, int latCells, int lonCells)
    {
        int latC = (int)((coord.Latitude + 90.0) / 180.0 * latCells);
        if (latC < 0) latC = 0;
        else if (latC >= latCells) latC = latCells - 1;

        int lonC = (int)((coord.Longitude + 180.0) / 360.0 * lonCells) % lonCells;
        if (lonC < 0) lonC += lonCells;

        return latC * lonCells + lonC;
    }

    /// <summary>
    /// Nearest tile center via the per-topology lat/lon bucket index. Falls back to
    /// the exhaustive scan only when the query neighborhood holds no candidates
    /// (possible solely for tiny topologies with empty cells).
    /// </summary>
    public int NearestTile(GeoCoord coord)
    {
        ThrowIfNotFinite(coord);
        var target = Vector3D.FromGeoCoord(coord);

        int latC = (int)((coord.Latitude + 90.0) / 180.0 * LatCells);
        if (latC < 0) latC = 0;
        else if (latC >= LatCells) latC = LatCells - 1;

        int lonC = (int)((coord.Longitude + 180.0) / 360.0 * LonCells) % LonCells;
        if (lonC < 0) lonC += LonCells;

        int bestIndex = -1;
        double bestScore = double.NegativeInfinity;
        int latRadius = LatRadiusCells;

        // Meridians converge toward the poles, so the same physical covering angle
        // spans more longitude cells there. When the latitude window touches a polar
        // row, scan the full circle: longitude is degenerate at the poles, so a tile
        // in the polar cap (e.g. the pole tile itself) can sit at any longitude while
        // being a fraction of a degree away. Otherwise use the smallest cosine over
        // the window rows (no clamp: the polar case is handled above, so cos > 0).
        double rowH = 180.0 / LatCells;
        int topRow = Math.Min(latC + latRadius, LatCells - 1);
        int botRow = Math.Max(latC - latRadius, 0);
        int lonRadius;
        if (topRow == LatCells - 1 || botRow == 0)
        {
            lonRadius = LonCells;
        }
        else
        {
            double edgeLat = Math.Max(Math.Abs((topRow + 1) * rowH - 90.0), Math.Abs(botRow * rowH - 90.0));
            double cosEdge = Math.Abs(DetMath.Cos(edgeLat * GeoCoord.Deg2Rad));
            lonRadius = (int)Math.Ceiling(CoverDeg / (LonCellDeg * cosEdge)) + 1;
            if (lonRadius >= LonCells) lonRadius = LonCells;
        }

        // When the window wraps the whole circle, scan each column exactly once
        // (otherwise polar rows were visited twice).
        int lonStart = -lonRadius, lonEnd = lonRadius;
        if (2 * lonRadius + 1 >= LonCells)
        {
            lonStart = 0;
            lonEnd = LonCells - 1;
            lonC = 0;
        }

        for (int dLa = -latRadius; dLa <= latRadius; dLa++)
        {
            int la = latC + dLa;
            if (la < 0 || la >= LatCells) continue;
            for (int dLo = lonStart; dLo <= lonEnd; dLo++)
            {
                int lo = (lonC + dLo) % LonCells;
                if (lo < 0) lo += LonCells;
                int cell = la * LonCells + lo;
                for (int k = CellOffsets[cell]; k < CellOffsets[cell + 1]; k++)
                {
                    int tile = CellTiles[k];
                    var score = Vector3D.Dot(target, TileVectors[tile]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = tile;
                    }
                }
            }
        }

        // Non-finite input is rejected up front, so a miss here can only mean an
        // empty neighborhood (tiny topologies): fall back to the exhaustive scan.
        return bestIndex >= 0 ? bestIndex : NearestTileExact(coord);
    }

    // NaN/Infinity used to resolve silently to tile 0 (every dot score lost),
    // handing callers a plausible-looking but wrong tile.
    private static void ThrowIfNotFinite(GeoCoord coord)
    {
        if (!double.IsFinite(coord.Latitude) || !double.IsFinite(coord.Longitude))
            throw new ArgumentOutOfRangeException(nameof(coord), coord, "Latitude and longitude must be finite.");
    }

    /// <summary>
    /// Exhaustive nearest-center search. The verification path for <see cref="NearestTile"/>.
    /// </summary>
    public int NearestTileExact(GeoCoord coord)
    {
        ThrowIfNotFinite(coord);
        var target = Vector3D.FromGeoCoord(coord);
        int bestIndex = 0;
        double bestScore = double.NegativeInfinity;

        var vecs = TileVectors;
        for (int i = 0; i < vecs.Length; i++)
        {
            var score = Vector3D.Dot(target, vecs[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
