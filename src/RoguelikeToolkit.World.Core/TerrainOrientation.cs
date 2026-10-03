namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Static sampler that derives a <see cref="ParentContext"/> for one planet
/// tile from dense store spans plus adjacency. Loops the actual ring size, so
/// 5-neighbor pentagon tiles and 6-neighbor hex tiles share one code path.
/// Missing layers degrade to documented defaults (0 elevation, 0.5
/// temperature/precipitation, Plains biome, contour-only strike).
/// </summary>
public static class TerrainOrientation
{
    private const double Epsilon = 1e-9;

    public static ParentContext Sample(WorldDataStore store, int tileIndex)
    {
        ArgumentNullException.ThrowIfNull(store);
        if ((uint)tileIndex >= (uint)store.TileCount) throw new IndexOutOfRangeException();

        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        bool hasLocal = store.IsLayerRegistered<LocalMapInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        bool hasPlates = store.IsLayerRegistered<TectonicPlate>();

        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        var locals = hasLocal ? store.GetSpan<LocalMapInfo>() : default;
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        var plates = hasPlates ? store.GetSpan<TectonicPlate>() : default;
        if (hasHydro)
        {
            // Registered-but-unrun hydro reads all zeros: treat as absent so
            // entry/exit fall back to elevation (mirrors ClimateStage).
            bool hydroRan = false;
            for (int s = 0; s < store.TileCount; s++)
            {
                if (hydro[s].Flow != 0f || hydro[s].Surface != 0f) { hydroRan = true; break; }
            }
            if (!hydroRan) hasHydro = false;
        }

        Span<int> neighbors = stackalloc int[6];
        int count = store.GetAdjacent(tileIndex, neighbors);

        var vectors = store.GetTileVectors();
        var up = vectors[tileIndex];

        // Local east/north frame (matches ClimateStage's zonal convention).
        var geo = up.ToGeoCoord();
        double lonR = geo.LongitudeRad;
        var east = new Vector3D(-DetMath.Sin(lonR), DetMath.Cos(lonR), 0);
        if (east.Length < Epsilon) east = new Vector3D(1, 0, 0);
        east = east.Normalize();
        var north = Vector3D.Cross(up, east).Normalize();

        static float Height(Span<ElevationInfo> e, bool has, int i) => has ? e[i].Height : 0f;
        static float Surface(Span<HydrologyInfo> h, Span<ElevationInfo> e, bool hasHydro, bool hasElev, int i)
            => hasHydro ? h[i].Surface : (hasElev ? e[i].Height : 0f);

        // Ring + means.
        var ring = new float[count];
        double sumElev = Height(elev, hasElev, tileIndex), sumTemp = 0, sumPrecip = 0;
        double windX = 0, windY = 0, windZ = 0;
        var biomeVotes = new Dictionary<BiomeType, int>();

        void Vote(BiomeType b) => biomeVotes[b] = biomeVotes.TryGetValue(b, out int n) ? n + 1 : 1;

        if (hasClimate)
        {
            sumTemp = climate[tileIndex].Temperature;
            sumPrecip = climate[tileIndex].Precipitation;
            windX = climate[tileIndex].WindX; windY = climate[tileIndex].WindY; windZ = climate[tileIndex].WindZ;
        }
        else
        {
            sumTemp = 0.5; sumPrecip = 0.5;
        }
        if (hasLocal) Vote(locals[tileIndex].Biome);

        for (int k = 0; k < count; k++)
        {
            int j = neighbors[k];
            ring[k] = Height(elev, hasElev, j);
            sumElev += ring[k];
            if (hasClimate)
            {
                sumTemp += climate[j].Temperature;
                sumPrecip += climate[j].Precipitation;
                windX += climate[j].WindX; windY += climate[j].WindY; windZ += climate[j].WindZ;
            }
            else
            {
                sumTemp += 0.5; sumPrecip += 0.5;
            }
            if (hasLocal) Vote(locals[j].Biome);
        }

        double n = count + 1;
        var wind = new Vector3D(windX / n, windY / n, windZ / n);
        if (wind.Length > Epsilon) wind = wind.Normalize();

        BiomeType dominant = BiomeType.Plains;
        int bestVotes = -1;
        foreach (var (biome, votes) in biomeVotes)
        {
            if (votes > bestVotes) { bestVotes = votes; dominant = biome; }
        }

        // Least-squares gradient of height over tangent-plane offsets.
        // M = sum p p^T, b = sum dh p, for p = (e, n) tangent coords.
        double mEE = 0, mEN = 0, mNN = 0, bE = 0, bN = 0;
        float h0 = Height(elev, hasElev, tileIndex);
        for (int k = 0; k < count; k++)
        {
            int j = neighbors[k];
            var t = vectors[j] - up * Vector3D.Dot(vectors[j], up);
            double e = Vector3D.Dot(t, east);
            double nn = Vector3D.Dot(t, north);
            double dh = ring[k] - h0;
            mEE += e * e; mEN += e * nn; mNN += nn * nn;
            bE += dh * e; bN += dh * nn;
        }
        double det = mEE * mNN - mEN * mEN;
        double gE = 0, gN = 0;
        if (Math.Abs(det) > Epsilon)
        {
            gE = (bE * mNN - bN * mEN) / det;
            gN = (mEE * bN - mEN * bE) / det;
        }
        var gradient = east * gE + north * gN;

        double aspect = 0;
        if (gradient.Length > Epsilon)
        {
            var downhill = gradient * -1;
            aspect = DetMath.Atan2(Vector3D.Dot(downhill, east), Vector3D.Dot(downhill, north));
        }

        // Strike: contour direction (tangent-perpendicular of the gradient),
        // aligned with the drift-perpendicular when tectonics are present.
        var contour = Perp(up, gradient.Length > Epsilon ? gradient.Normalize() : east);
        Vector3D strike = contour;
        if (hasPlates)
        {
            var drift = new Vector3D(plates[tileIndex].DriftX, plates[tileIndex].DriftY, plates[tileIndex].DriftZ);
            var tangent = drift - up * Vector3D.Dot(drift, up);
            if (tangent.Length > 1e-6)
            {
                var driftStrike = Perp(up, tangent.Normalize());
                if (Vector3D.Dot(driftStrike, contour) < 0) driftStrike = driftStrike * -1;
                strike = driftStrike;
            }
        }

        // Drainage on the routing surface: entry = highest neighbor,
        // exit = lowest neighbor when strictly below center, else sink (-1).
        int entry = -1, exit = -1;
        float s0 = Surface(hydro, elev, hasHydro, hasElev, tileIndex);
        float maxS = float.NegativeInfinity, minS = float.PositiveInfinity;
        for (int k = 0; k < count; k++)
        {
            float s = Surface(hydro, elev, hasHydro, hasElev, neighbors[k]);
            if (s > maxS) { maxS = s; entry = neighbors[k]; }
            if (s < minS) { minS = s; exit = neighbors[k]; }
        }
        if (count == 0) entry = -1;
        if (count == 0 || minS >= s0) exit = -1;

        bool isRiver = hasHydro && hydro[tileIndex].IsRiver == 1;
        float flow = hasHydro ? hydro[tileIndex].Flow : 0f;

        return new ParentContext
        {
            MeanElevation = (float)(sumElev / n),
            DominantBiome = hasLocal ? dominant : BiomeType.Plains,
            MeanTemperature = (float)(sumTemp / n),
            MeanPrecipitation = (float)(sumPrecip / n),
            MeanMoisture = (float)(sumPrecip / n),
            ElevationGradient = gradient,
            AspectRadians = aspect,
            OrogenyStrike = strike,
            NeighborHeights = ring,
            FlowEntryTile = entry,
            FlowExitTile = exit,
            Wind = wind,
            IsRiver = isRiver,
            Flow = flow,
        };
    }

    /// <summary>Unit tangent vector perpendicular to w (90-degree rotation about up).</summary>
    private static Vector3D Perp(Vector3D up, Vector3D w)
    {
        var p = Vector3D.Cross(up, w);
        return p.Length > Epsilon ? p.Normalize() : w;
    }
}
