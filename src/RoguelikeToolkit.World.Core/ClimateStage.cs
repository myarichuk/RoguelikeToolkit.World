using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// First-class climate layer. Temperature from latitude + lapse rate +
/// continentality; precipitation from seamless noise modulated by orographic
/// effects (windward wet / leeward rain shadow) and water proximity; wind as a
/// zonal prevailing field (trades / westerlies / polar easterlies).
/// Runs after elevation (15) and before erosion (17) so rainfall can seed
/// runoff. The hydrology read is optional: when hydrology has not run yet the
/// water-proximity bonus is skipped.
/// </summary>
[WorldGeneratorStage(16, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(HydrologyInfo) }, Writes = new[] { typeof(ClimateInfo) })]
public class ClimateStage : IWorldGeneratorStage, ISeededStage
{
    public int Seed { get; set; } = 42;
    public double LapseRate { get; set; } = 0.35;
    public Vector3D PrevailingWindFallback { get; set; } = new Vector3D(1, 0, 0);

    public ClimateStage() { }
    public ClimateStage(int seed = 42) { Seed = seed; }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.IsLayerRegistered<HydrologyInfo>() ? store.GetSpan<HydrologyInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
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
            var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0).Normalize();
            double absLat = Math.Abs(geo.Latitude);
            double dir = absLat < 30.0 ? -1.0 : (absLat < 60.0 ? 1.0 : -1.0);
            var wind = east * dir;

            // Base precipitation from independent noise field.
            double moisture = SphereNoise.Fbm(vectors[i] * 2.0 + moistureOffset, Seed + 1000) * 0.5 + 0.5;

            // Orographic effect: compare against the most upwind neighbor.
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
            if (upwind >= 0 && bestUp > 0.15)
            {
                double dh = height - elev[upwind].Height;
                if (dh > 0)
                    moisture *= 1.0 + Math.Min(dh * 1.2, 0.6); // windward lift
                else
                    moisture *= 1.0 / (1.0 + Math.Min(-dh * 3.0, 0.65)); // leeward rain shadow
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
            temperature = Math.Clamp(temperature, 0.0, 1.0);

            climate[i] = new ClimateInfo
            {
                Temperature = (float)temperature,
                Precipitation = (float)moisture,
                WindX = (float)wind.X,
                WindY = (float)wind.Y,
                WindZ = (float)wind.Z
            };
        }
    }
}
