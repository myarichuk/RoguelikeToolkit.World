using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// First-class climate layer. Temperature from latitude and the elevation
/// lapse rate only (no continentality or ocean-heat term); precipitation from seamless noise modulated by orographic
/// effects (windward wet / leeward rain shadow) and water proximity; wind as a
/// zonal prevailing field (trades / westerlies / polar easterlies).
/// Precipitation is box-blurred (PrecipSmoothingPasses, default 1, backlog
/// E6) to kill tile-scale speckle while keeping the rain shadow.
/// Runs after elevation (15) and before erosion (17) so rainfall can seed
/// runoff. Ordering circularity: hydrology (18) needs climate rainfall, while
/// climate would like lake proximity from hydrology — so climate runs first
/// and treats a registered-but-unrun hydro layer (all Flow/Surface zero) as
/// absent, falling back to ocean-elevation adjacency. Lakes therefore do not
/// feed moisture on the first pass; rerunning this stage after hydrology picks
/// up lake adjacency where a host explicitly orders a second pass.
/// </summary>
[WorldGeneratorStage(16, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(HydrologyInfo) }, Writes = new[] { typeof(ClimateInfo) })]
public class ClimateStage : IWorldGeneratorStage, ISeededStage
{
    public int Seed { get; set; } = 42;
    public double LapseRate { get; set; } = 0.35;
    public Vector3D PrevailingWindFallback { get; set; } = new Vector3D(1, 0, 0);

    /// <summary>
    /// Box-blur passes over the base moisture field (backlog E6), applied
    /// before the orographic + water terms: each pass replaces a tile's base
    /// precip with the mean of itself + its neighbors. Default 1: cuts
    /// tile-scale noise speckle while the rain shadow keeps full contrast
    /// (orographic gains apply to the blurred field, unblurred).
    /// Deterministic; 0 disables.
    /// NOTE: smoothing shifts the precip distribution DryPolarGate was
    /// calibrated against — re-measure the biome matrix after changing this.
    /// </summary>
    public int PrecipSmoothingPasses { get; set; } = 1;

    /// <summary>Upwindness weight ramps 0 at/below Lo, 1 at/above Hi.</summary>
    private const double UpwindGateLo = 0.05;
    private const double UpwindGateHi = 0.25;

    public ClimateStage() { }
    public ClimateStage(int seed = 42) { Seed = seed; }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        if (hasHydro)
        {
            // Registered-but-unrun hydro reads all zeros (notably WaterBodyId 0,
            // which would fake "near water" everywhere). One O(n) scan treats an
            // un-run layer as absent so the elevation fallback applies instead.
            bool ran = false;
            for (int s = 0; s < store.TileCount; s++)
            {
                if (hydro[s].Flow != 0f || hydro[s].Surface != 0f) { ran = true; break; }
            }
            if (!ran) hasHydro = false;
        }
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();

        var moistureOffset = new Vector3D(13.7, -7.3, 5.1);
        Span<int> neighbors = stackalloc int[6];

        for (int i = 0; i < store.TileCount; i++)
        {
            double height = elev[i].Height;
            var geo = vectors[i].ToGeoCoord();

            double temperature = 1.0 - Math.Abs(geo.Latitude) / 90.0;
            temperature -= Math.Max(0.0, height) * LapseRate;

            // Zonal prevailing wind: trades (tropics, east->west), westerlies
            // (mid latitudes), polar easterlies. Tangent east vector at position.
            double lonR = geo.LongitudeRad;
            var east = new Vector3D(-DetMath.Sin(lonR), DetMath.Cos(lonR), 0).Normalize();
            double absLat = Math.Abs(geo.Latitude);
            double dir = absLat < 30.0 ? -1.0 : (absLat < 60.0 ? 1.0 : -1.0);
            var wind = east * dir;

            // Base precipitation from independent noise field. Stored now;
            // orographic + water terms apply in a second pass over the
            // blurred field, so the rain shadow keeps full contrast while
            // the noise speckle is gone (backlog E6).
            double moisture = SphereNoise.Fbm(vectors[i] * 2.0 + moistureOffset, Seed + 1000) * 0.5 + 0.5;
            climate[i] = new ClimateInfo
            {
                Temperature = (float)Math.Clamp(temperature, 0.0, 1.0),
                Precipitation = (float)Math.Clamp(moisture, 0.0, 1.0),
                WindX = (float)wind.X,
                WindY = (float)wind.Y,
                WindZ = (float)wind.Z
            };
        }

        for (int pass = 0; pass < PrecipSmoothingPasses; pass++)
            BlurPrecipitation(map);

        for (int i = 0; i < store.TileCount; i++)
        {
            double height = elev[i].Height;
            double moisture = climate[i].Precipitation;
            var wind = new Vector3D(climate[i].WindX, climate[i].WindY, climate[i].WindZ);

            // Orographic effect: compare against the most upwind neighbor.
            // The upwind gate is continuous (E6 smoothstep): the old hard
            // 0.15 cutoff's on/off flips between adjacent tiles were
            // tile-scale speckle. Peak gains are unchanged (x1.6 windward,
            // /1.65 leeward), so the rain shadow keeps full contrast.
            // (A blended-over-neighbors variant was tried and reverted: it
            // cost 30-70% of the shadow depth and half the seed-42 deserts
            // for only ~20% less same-side speckle.)
            int adjacent = store.GetAdjacent(i, neighbors);
            int upwind = -1;
            double bestUp = double.NegativeInfinity;
            for (int k = 0; k < adjacent; k++)
            {
                int j = neighbors[k];
                var tang = (vectors[j] - vectors[i]).Normalize();
                double along = Vector3D.Dot(tang, wind);
                double up = -along; // positive when j is upwind of i
                if (up > bestUp) { bestUp = up; upwind = j; }
            }
            if (upwind >= 0 && bestUp > UpwindGateLo)
            {
                double gt = (bestUp - UpwindGateLo) / (UpwindGateHi - UpwindGateLo);
                if (gt > 1.0) gt = 1.0;
                double gw = gt * gt * (3.0 - 2.0 * gt);
                double dh = height - elev[upwind].Height;
                if (dh > 0)
                    moisture *= 1.0 + gw * Math.Min(dh * 1.2, 0.6); // windward lift
                else
                    moisture *= 1.0 + gw * (1.0 / (1.0 + Math.Min(-dh * 3.0, 0.65)) - 1.0); // leeward rain shadow
            }

            // Water proximity bonus (lakes/seas/ocean or ocean elevation).
            bool nearWater = false;
            if (hasHydro && hydro[i].WaterBodyId >= 0) nearWater = true;
            else
            {
                for (int k = 0; k < adjacent && !nearWater; k++)
                {
                    int j = neighbors[k];
                    if (hasHydro && hydro[j].WaterBodyId >= 0) nearWater = true;
                    else if (elev[j].Height < ElevationGenerationStage.SeaLevel) nearWater = true;
                }
            }
            if (nearWater) moisture += 0.06;
            if (height < ElevationGenerationStage.SeaLevel) moisture = Math.Max(moisture, 0.55);

            moisture = Math.Clamp(moisture, 0.0, 1.0);

            var updated = climate[i];
            updated.Precipitation = (float)moisture;
            climate[i] = updated;
        }
    }

    /// <summary>
    /// One box-blur pass over the base moisture field (backlog E6): each
    /// tile becomes the mean of itself + its neighbors. Runs before the
    /// orographic + water terms, so only noise speckle is smoothed and the
    /// rain-shadow contrast is preserved. Deterministic (fixed traversal
    /// order, pure function of the stored field).
    /// </summary>
    private static void BlurPrecipitation(WorldMap map)
    {
        var store = map.DataStore;
        var climate = store.GetSpan<ClimateInfo>();
        var blurred = new float[store.TileCount];
        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < store.TileCount; i++)
        {
            int adjacent = store.GetAdjacent(i, neighbors);
            float sum = climate[i].Precipitation;
            for (int k = 0; k < adjacent; k++) sum += climate[neighbors[k]].Precipitation;
            blurred[i] = sum / (adjacent + 1);
        }
        for (int i = 0; i < store.TileCount; i++)
        {
            var c = climate[i];
            c.Precipitation = Math.Clamp(blurred[i], 0f, 1f);
            climate[i] = c;
        }
    }
}
