namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Spatial extent of one map: its center, a bounding radius in km, and the
/// edge (border-ring) tile indices in that map's own indexing.
/// </summary>
public readonly record struct MapBounds(GeoCoord Center, double RadiusKm, int[] EdgeTiles)
{
    // The synthesized record equality would compare EdgeTiles by reference, so two
    // bounds describing the same ring would be unequal. Compare by content.
    public bool Equals(MapBounds other)
        => Center.Equals(other.Center)
        && RadiusKm.Equals(other.RadiusKm)
        && (ReferenceEquals(EdgeTiles, other.EdgeTiles)
            || (EdgeTiles != null && other.EdgeTiles != null && EdgeTiles.AsSpan().SequenceEqual(other.EdgeTiles)));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Center);
        hash.Add(RadiusKm);
        if (EdgeTiles != null)
            foreach (int tile in EdgeTiles) hash.Add(tile);
        return hash.ToHashCode();
    }

    /// <summary>Bounds of the hex tile itself: neighbors form its edge ring.</summary>
    public static MapBounds ForPlanetTile(WorldDataStore store, int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(store);
        if ((uint)tileIndex >= (uint)store.TileCount) throw new IndexOutOfRangeException();

        var center = store.GetGeoCoord(tileIndex);
        var vectors = store.GetTileVectors();
        var up = vectors[tileIndex];

        Span<int> neighbors = stackalloc int[6];
        int count = store.GetAdjacent(tileIndex, neighbors);

        double maxAngle = 0.0;
        var edge = new int[count];
        for (int k = 0; k < count; k++)
        {
            edge[k] = neighbors[k];
            double dot = Math.Clamp(Vector3D.Dot(up, vectors[neighbors[k]]), -1.0, 1.0);
            maxAngle = Math.Max(maxAngle, DetMath.Acos(dot));
        }

        return new MapBounds(center, maxAngle * 0.5 * World.EarthRadiusKm, edge);
    }

    /// <summary>Bounds of a size x size square grid map (region/local).</summary>
    public static MapBounds ForGrid(GeoCoord center, double radiusKm, int size)
    {
        if (radiusKm < 0) throw new ArgumentOutOfRangeException(nameof(radiusKm));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        var edge = new List<int>(4 * (size - 1));
        int last = size - 1;
        for (int q = 0; q < size; q++)
        {
            edge.Add(q); // top row r=0
            if (last > 0) edge.Add(last * size + q); // bottom row
        }
        for (int r = 1; r < last; r++)
        {
            edge.Add(r * size); // left column
            edge.Add(r * size + last); // right column
        }

        return new MapBounds(center, radiusKm, edge.ToArray());
    }
}
