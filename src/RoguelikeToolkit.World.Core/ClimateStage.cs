using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// First-class climate layer. Temperature from latitude, the planet's heat budget and the elevation
/// lapse rate, plus the heat the ocean currents bring to the coasts (<see cref="OceanCurrents"/>); precipitation from seamless noise modulated by orographic
/// effects (windward wet / leeward rain shadow) and water proximity; wind as a
/// zonal prevailing field (trades / westerlies / polar easterlies).
/// Maritime moisture is advected from every water tile along the prevailing wind
/// (<see cref="ComputeMaritimeAir"/>), decays with distance travelled and is wrung
/// out crossing high ground, so coasts and windward slopes are wet while
/// continental interiors and rain shadows are dry. Without it aridity is plain
/// noise and lands on the shoreline as often as in a continent's heart.
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
[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(16, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(HydrologyInfo), typeof(UserEditInfo) }, Writes = new[] { typeof(ClimateInfo) })]
public class ClimateStage : IWorldGeneratorStage, ISeededStage
{
    public int Seed { get; set; } = 42;
    public double LapseRate { get; set; } = ClimateModel.DefaultLapseRate;

    /// <summary>
    /// Axial tilt, stellar flux and spin of the planet (see <see cref="PlanetParameters"/>). The default is
    /// Earth, which takes the legacy arithmetic exactly.
    /// </summary>
    public PlanetParameters Planet { get; set; } = new PlanetParameters();

    /// <summary>
    /// Share of the surface wind that blows along the meridian (towards the equator in the Hadley and
    /// polar cells, towards the pole in the Ferrel cell), as the Coriolis force turns the pressure-gradient
    /// flow: the north-east trades and the south-westerlies instead of due-east/due-west. 0 keeps the
    /// wind purely zonal. 0.3 (about 17 degrees off the parallel) is the default: measured over seven
    /// worlds it leaves mean temperature, rainfall and the biome shares within half a point, and turns the
    /// rain shadows and maritime plumes diagonal as they are on Earth.
    /// </summary>
    public double MeridionalWind { get; set; } = 0.3;

    /// <summary>
    /// How far the wind speed fades to a calm at the cell boundaries (the horse latitudes near 30 degrees,
    /// the subpolar low near 60), 0..1. 0 is the legacy field: unit speed everywhere, reversing in one step
    /// across the boundary. 1 makes the speed follow sin(pi * position-in-cell), zero at both edges and
    /// strongest mid-cell (the trades near 15 degrees, the westerlies near 45), keeping the direction inside
    /// each cell. The wind vector's length is what the orographic gate and the maritime hop weigh, so a calm
    /// belt lifts and shadows less and lets moisture diffuse in every direction.
    /// <para>
    /// Off by default because it is not free: the desert and rain calibration was tuned at 0, and wind
    /// erosion reads the same length (a tile needs 0.15 of wind along an edge to shed sand), so the calm
    /// belts, which are where the dunes are, shed less. Measured on seven worlds at size 4 and 5, a full
    /// blend moves 39-56% less sand and a half blend 15-25% less, with a smaller share of it in the 20-40
    /// degree belt.
    /// </para>
    /// </summary>
    public double WindCellBlend { get; set; } = 0.0;
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

    /// <summary>
    /// E-folding distance of maritime air over land, in radians of arc (about 5700 km on an
    /// Earth-sized planet) along the wind. Angular, so the same world reads the same at any
    /// icosphere size.
    /// </summary>
    public double MoistureReach { get; set; } = 0.9;

    /// <summary>Moisture surviving a climb: factor = exp(-ClimbLoss * rise). 0 disables the extra orographic loss.</summary>
    public double ClimbLoss { get; set; } = 2.5;

    /// <summary>Precipitation multiplier in dead-still continental air (maritime = 0).</summary>
    public double InteriorFactor { get; set; } = 0.76;

    /// <summary>Extra multiplier gained at full maritime influence (factor = Interior + Gain * maritime).</summary>
    public double MaritimeGain { get; set; } = 0.56;

    /// <summary>
    /// Strength of the zonal (Hadley-cell) moisture banding, see <see cref="ZonalFactor(double, double)"/>:
    /// wet equatorial convergence, a dry belt of subtropical highs near 27 degrees, wetter
    /// mid-latitude storm tracks and dry poles. 1 is the calibrated default, 0 turns the banding
    /// off (precipitation then has no latitude structure beyond temperature).
    /// </summary>
    public double ZonalStrength { get; set; } = 1.0;

    /// <summary>
    /// How much of the sea-surface temperature anomaly the ocean currents carry (see <see cref="OceanCurrents"/>) is added to
    /// the temperature, over the water and, scaled by the maritime air that reaches it, over coastal land: a warm
    /// current mellows the coast it runs along (north-west Europe), a cold one chills it. 1 is the calibrated
    /// strength (peaks of about 0.15 units, 8 K, along the strongest boundary currents); 0 turns the heat off.
    /// </summary>
    public double OceanHeatStrength { get; set; } = 1.0;

    /// <summary>
    /// Rainfall response to the current's temperature at the coast: the multiplier is 1 + gain * maritime * anomaly,
    /// clamped to [0.5, 1.4], so cold-current coasts under the subtropical highs get the fog deserts (Atacama,
    /// Namib) and warm ones the wet east coasts. 0 turns it off; with <see cref="OceanHeatStrength"/> also 0 no
    /// current is solved at all.
    /// </summary>
    public double OceanRainGain { get; set; } = 2.0;

    /// <summary>Upwindness weight ramps 0 at/below Lo, 1 at/above Hi.</summary>
    private const double UpwindGateLo = 0.05;
    private const double UpwindGateHi = 0.25;

    public ClimateStage() { }
    public ClimateStage(int seed = 42) { Seed = seed; }

    /// <summary>
    /// Set by the builder when a <see cref="ClimateRefreshStage"/> follows erosion: that pass overwrites the layer wholesale
    /// and everything downstream of erosion reads it, while the first pass only feeds erosion rainfall and
    /// temperature, to which a coastal current's few hundredths make no difference. The first pass then skips the
    /// ocean-current solve, which is most of this stage's cost on a fine world. A hand-built pipeline that leaves it
    /// false solves in both passes.
    /// </summary>
    internal bool CurrentsInRefreshPassOnly { get; set; }

    public void Execute(WorldMap map) => Run(map, !CurrentsInRefreshPassOnly);

    /// <summary>The post-erosion pass: always with the currents, on the relief the player sees.</summary>
    internal void ExecuteRefresh(WorldMap map) => Run(map, true);

    private void Run(WorldMap map, bool solveCurrents)
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

        var rawTemperature = new double[store.TileCount];
        bool hasEdits = store.IsLayerRegistered<UserEditInfo>();
        var edits = hasEdits ? store.GetSpan<UserEditInfo>() : default;
        var moistureOffset = new Vector3D(13.7, -7.3, 5.1);
        Span<int> neighbors = stackalloc int[6];
        var planet = Planet ?? new PlanetParameters();
        double bandScale = planet.BandScale;

        for (int i = 0; i < store.TileCount; i++)
        {
            double height = elev[i].Height;
            var geo = vectors[i].ToGeoCoord();

            double absLat = Math.Abs(geo.Latitude);
            double temperature = ClimateModel.Temperature(vectors[i], absLat, height, LapseRate, planet);
            rawTemperature[i] = temperature;

            // Zonal prevailing wind: trades (tropics, east->west), westerlies
            // (mid latitudes), polar easterlies. The cells alternate every 30 degrees on Earth and are
            // narrower on a fast spinner, wider on a slow one. Tangent east vector at position.
            double lonR = geo.LongitudeRad;
            var east = new Vector3D(-DetMath.Sin(lonR), DetMath.Cos(lonR), 0).Normalize();
            int cell = (int)(Math.Min(absLat, 90.0 - 1e-9) / (30.0 * bandScale)); // the pole itself belongs to the last cell
            bool evenCell = (cell & 1) == 0;
            double dir = evenCell ? -1.0 : 1.0;
            var wind = east * dir;
            if (MeridionalWind > 0.0)
            {
                // Surface return flow of the Hadley and polar cells runs towards the equator, the
                // Ferrel cell's towards the pole; east x north = up, so north = up x east.
                var north = Vector3D.Cross(vectors[i], east).Normalize();
                double hemisphere = geo.Latitude >= 0.0 ? 1.0 : -1.0;
                wind = (east * dir + north * ((evenCell ? -1.0 : 1.0) * hemisphere * MeridionalWind)).Normalize();
            }
            if (WindCellBlend > 0.0)
            {
                double cellPos = absLat / (30.0 * bandScale) - cell; // 0..1 across the cell
                double speed = DetMath.Sin(Math.PI * Math.Clamp(cellPos, 0.0, 1.0));
                wind *= 1.0 - WindCellBlend + WindCellBlend * Math.Max(0.0, speed);
            }

            // Base precipitation from independent noise field. Stored now;
            // orographic + water terms apply in a second pass over the
            // blurred field, so the rain shadow keeps full contrast while
            // the noise speckle is gone (backlog E6).
            double moisture = SphereNoise.Fbm(vectors[i] * 2.0 + moistureOffset, Seed + 1000) * 0.5 + 0.5;
            // Zonal banding scales the base field, so the maritime and orographic terms below still
            // act on top of it: a subtropical coast stays wetter than the continent behind it, but
            // the whole belt starts from a much drier baseline than the equator or the westerlies.
            moisture *= ZonalFactor(absLat, ZonalStrength, bandScale);
            climate[i] = new ClimateInfo
            {
                Temperature = (float)Math.Clamp(temperature, 0.0, 1.0),
                Precipitation = (float)Math.Clamp(moisture, 0.0, 1.0),
                WindX = (float)wind.X,
                WindY = (float)wind.Y,
                WindZ = (float)wind.Z,
                RainSeasonality = (float)ClimateModel.RainSeasonality(geo.Latitude, planet.ObliquityDegrees, ZonalStrength, bandScale),
                RainPhase = (float)ClimateModel.RainPhase(geo.Latitude, planet.ObliquityDegrees, ZonalStrength, bandScale),
            };
        }

        for (int pass = 0; pass < PrecipSmoothingPasses; pass++)
            BlurPrecipitation(map);

        bool currents = solveCurrents && (OceanHeatStrength > 0.0 || OceanRainGain > 0.0);
        var field = currents ? OceanCurrents.Solve(store, elev, vectors, planet, OceanCurrentSettings.Default) : null;
        var origin = new int[store.TileCount];
        var maritime = ComputeMaritimeAir(store, elev, climate, vectors, origin);

        for (int i = 0; i < store.TileCount; i++)
        {
            double height = elev[i].Height;
            double moisture = climate[i].Precipitation;
            if (height >= ElevationGenerationStage.SeaLevel)
                moisture *= InteriorFactor + MaritimeGain * maritime[i];
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

            // Ocean currents: land breathes the sea-surface temperature of the water its maritime air crossed.
            double heat = 0.0;
            if (field != null)
            {
                if (height < ElevationGenerationStage.SeaLevel) heat = field.Anomaly[i];
                else if (origin[i] >= 0)
                {
                    double carried = maritime[i] * field.Anomaly[origin[i]];
                    heat = carried;
                    if (OceanRainGain > 0.0)
                        moisture *= Math.Clamp(1.0 + OceanRainGain * carried, 0.5, 1.4);
                }
            }
            if (height < ElevationGenerationStage.SeaLevel) moisture = Math.Max(moisture, 0.55);

            moisture = Math.Clamp(moisture, 0.0, 1.0);

            var updated = climate[i];
            if (field != null)
            {
                double applied = OceanHeatStrength * heat;
                updated.Temperature = (float)Math.Clamp(rawTemperature[i] + applied, 0.0, 1.0);
                updated.OceanHeat = (float)applied;
                var cur = field.Velocity[i];
                updated.CurrentX = (float)cur.X;
                updated.CurrentY = (float)cur.Y;
                updated.CurrentZ = (float)cur.Z;
            }
            updated.SeasonalRange = (float)ClimateModel.Continental(ClimateModel.SeasonalAmplitude(vectors[i].Z, planet.ObliquityDegrees), maritime[i]);
            updated.Precipitation = (float)moisture;
            if (hasEdits)
            {
                // The author's brushed offsets are the last word: literal, local, after every physical term.
                updated.Temperature = Math.Clamp(updated.Temperature + edits[i].TemperatureDelta, 0f, 1f);
                updated.Precipitation = Math.Clamp(updated.Precipitation + edits[i].PrecipitationDelta, 0f, 1f);
            }
            climate[i] = updated;
        }
    }

    /// <summary>
    /// Zonal precipitation multiplier at an absolute latitude in degrees. The sum of four bumps
    /// around 1: the ITCZ (+, centred on the equator), the subtropical high-pressure belt (-,
    /// 27 degrees, where descending air makes the Sahara/Atacama/Australian deserts), the
    /// mid-latitude storm tracks (+, 55 degrees) and the polar cold-air desert (-, from 65).
    /// Floored at 0.2 so a strength above 1 cannot zero a band out.
    /// </summary>
    public static double ZonalFactor(double absLatitude, double strength = 1.0) => ZonalFactor(absLatitude, strength, 1.0);

    /// <summary>
    /// <see cref="ZonalFactor(double, double)"/> for a planet whose circulation cells are
    /// <paramref name="bandScale"/> times Earth's width (1 / the rotation rate, Held &amp; Hou): every band centre
    /// moves with it, and the bands widen with its square root. The dry belt stops at 75 degrees and the
    /// storm track at 82 so a very slow planet's bands do not slide off the pole. 1 is exactly Earth.
    /// </summary>
    public static double ZonalFactor(double absLatitude, double strength, double bandScale)
    {
        static double Bump(double x, double centre, double width) { double d = (x - centre) / width; return Math.Exp(-d * d); }
        double w = bandScale == 1.0 ? 1.0 : Math.Sqrt(bandScale);
        double polarStart = Math.Min(80.0, 65.0 * bandScale);
        double polarT = Math.Clamp((absLatitude - polarStart) / (25.0 * w), 0.0, 1.0);
        double polar = polarT * polarT * (3.0 - 2.0 * polarT);
        double delta = 0.30 * Bump(absLatitude, 0.0, 9.0 * w)
                     - 0.55 * Bump(absLatitude, Math.Min(75.0, 27.0 * bandScale), 8.0 * w)
                     + 0.12 * Bump(absLatitude, Math.Min(82.0, 55.0 * bandScale), 10.0 * w)
                     - 0.20 * polar;
        return Math.Max(0.2, 1.0 + strength * delta);
    }

    /// <summary>
    /// Maritime air in [0, 1] per tile: 1 over water, then carried to downwind land by a best-path
    /// relaxation (max over routes of the product of per-hop survival), so it is exact and
    /// independent of tile iteration order. A hop survives as exp(-chord / <see cref="MoistureReach"/>),
    /// is strongest along the wind (a quarter of that against it, so there is still some diffusion
    /// and sea breeze), and loses exp(-<see cref="ClimbLoss"/> * rise) climbing, which leaves dry air
    /// behind mountain ranges and cold, dry continental interiors. When <paramref name="origin"/> is given it
    /// receives, per tile, the water tile its best path started from (-1 where no maritime air arrives), so the
    /// ocean currents' temperature can follow the air onto the coast.
    /// </summary>
    internal ReadOnlySpan<double> ComputeMaritimeAir(WorldDataStore store, Span<ElevationInfo> elev, Span<ClimateInfo> climate, ReadOnlySpan<Vector3D> vectors, Span<int> origin = default)
    {
        int n = store.TileCount;
        var air = new double[n];
        var queue = new PriorityQueue<int, double>();
        bool track = !origin.IsEmpty;
        for (int i = 0; i < n; i++)
        {
            if (track) origin[i] = -1;
            if (elev[i].Height >= ElevationGenerationStage.SeaLevel) continue;
            if (track) origin[i] = i;
            air[i] = 1.0;
            queue.Enqueue(i, -1.0);
        }
        Span<int> neighbors = stackalloc int[6];
        while (queue.TryDequeue(out int i, out double prio))
        {
            if (-prio < air[i]) continue; // stale entry
            int adjacent = store.GetAdjacent(i, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int j = neighbors[k];
                if (elev[j].Height < ElevationGenerationStage.SeaLevel) continue; // already a source
                var step = vectors[j] - vectors[i];
                double chord = step.Length;
                if (chord <= 0.0) continue;
                var wind = new Vector3D(climate[j].WindX, climate[j].WindY, climate[j].WindZ);
                double along = Math.Clamp(Vector3D.Dot(step * (1.0 / chord), wind), -1.0, 1.0);
                double hop = Math.Exp(-chord / MoistureReach) * (0.25 + 0.75 * (along + 1.0) * 0.5);
                double rise = elev[j].Height - Math.Max(elev[i].Height, ElevationGenerationStage.SeaLevel);
                if (rise > 0.0) hop *= Math.Exp(-ClimbLoss * rise);
                double candidate = air[i] * hop;
                if (candidate > air[j])
                {
                    air[j] = candidate;
                    if (track) origin[j] = origin[i]; // the water this air mass came from
                    queue.Enqueue(j, -candidate);
                }
            }
        }
        return air;
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
