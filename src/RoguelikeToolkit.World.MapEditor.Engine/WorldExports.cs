using System.Runtime.InteropServices.JavaScript;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.Names;
using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;
using CoreWorld = RoguelikeToolkit.World.Core.World;

namespace RoguelikeToolkit.World.MapEditor.Engine;

/// <summary>
/// The worker-facing surface. Generation runs once; the JS side then pulls flat arrays
/// (topology, fields, sparse catalogs) and does all rasterization/rendering itself. Nothing here
/// knows about pixels: Core hands over its native tile graph, the map editor owns the rest.
/// <para>
/// Interop shape: every call returns one primitive array. Sparse catalogs are flattened into
/// int arrays (<c>[count, (header..., n, items...)...]</c>) because one marshalled array is
/// cheaper than thousands of tiny ones, and JS decodes them with a cursor in a few lines.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public static partial class WorldExports
{
    private static CoreWorld? _world;
    private static TriangleIndices[]? _faces;

    [JSExport]
    public static int Generate(int seed, int size, int plates, double obliquity, double flux, double rotation,
        bool climateRefresh, double meridionalWind, double windCellBlend, double oceanHeat, double oceanRain, int aeolian)
    {
        // Validate before disposing: a bad request must not leave the editor with no world behind it.
        // Written as !(in range) so NaN is rejected too (every comparison with NaN is false).
        if (!(windCellBlend >= 0 && windCellBlend <= 1))
            throw new ArgumentOutOfRangeException(nameof(windCellBlend), "Wind cell blend must be within 0..1.");
        if (!(meridionalWind >= 0) || !(oceanHeat >= 0) || !(oceanRain >= 0) || aeolian < 0)
            throw new ArgumentOutOfRangeException(nameof(meridionalWind), "Climate model strengths cannot be negative.");
        var planet = new PlanetParameters { ObliquityDegrees = obliquity, StellarFlux = flux, RotationRate = rotation };
        _world?.Dispose();
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithUserEdits().WithLandUse().WithPlanet(planet)
            .WithClimateRefresh(climateRefresh)
            .WithClimate(c => { c.MeridionalWind = meridionalWind; c.WindCellBlend = windCellBlend; c.OceanHeatStrength = oceanHeat; c.OceanRainGain = oceanRain; })
            .WithErosion(e => e.AeolianIterations = aeolian);
        // The unrefreshed pipeline was calibrated at a river threshold of 1; 0.92 is for the refreshed climate (see WithClimateRefresh).
        if (!climateRefresh) builder.WithHydrology(h => h.RiverThresholdScale = 1f);
        if (plates > 0) builder.WithPlateCount(plates);
        _world = builder.Build();
        // The store discards faces after deriving adjacency; regenerate them (deterministic, same vertex order).
        IcosphereGenerator.Generate(size, out _, out _faces);
        return _world.TileCount;
    }

    /// <summary>
    /// Replaces the world's hand edits wholesale and re-derives everything downstream (see
    /// <see cref="CoreWorld.Recompute"/>). The table is sparse and absolute, so the call is
    /// idempotent: the engine keeps no history, the document owns the edits. <paramref name="meta"/>
    /// packs <see cref="UserEditFlags"/> in bits 0-7, the pinned <see cref="BiomeType"/> in 8-15, the pinned
    /// <see cref="LandUseInfo.Use"/> in 16-23 and its <see cref="LandUseInfo.Activities"/> in 24-31.
    /// <paramref name="temperature"/> and <paramref name="precipitation"/> are the brushed climate offsets
    /// (<see cref="UserEditInfo.TemperatureDelta"/>, <see cref="UserEditInfo.PrecipitationDelta"/>).
    /// </summary>
    [JSExport]
    public static int ApplyEdits(int[] tiles, double[] heightDelta, double[] pinHeight, int[] meta, double[] temperature, double[] precipitation)
    {
        var world = Current;
        if (tiles.Length != heightDelta.Length || tiles.Length != pinHeight.Length || tiles.Length != meta.Length
            || tiles.Length != temperature.Length || tiles.Length != precipitation.Length)
            throw new ArgumentException("Edit arrays must have equal lengths.");
        var edits = world.Edits;
        edits.Clear();
        for (int k = 0; k < tiles.Length; k++)
        {
            int t = tiles[k];
            if ((uint)t >= (uint)edits.Length) throw new ArgumentOutOfRangeException(nameof(tiles), t, "Tile index out of range.");
            edits[t] = new UserEditInfo
            {
                HeightDelta = (float)heightDelta[k],
                PinHeight = (float)pinHeight[k],
                Flags = (UserEditFlags)(meta[k] & 0xff),
                PinBiome = (BiomeType)((meta[k] >> 8) & 0xff),
                LandUse = (byte)((meta[k] >> 16) & 0xff),
                LandActivities = (byte)((meta[k] >> 24) & 0xff),
                TemperatureDelta = (float)temperature[k],
                PrecipitationDelta = (float)precipitation[k],
            };
        }
        world.Recompute();
        return world.TileCount;
    }

    /// <summary>Unit vectors of tile centers, xyz interleaved.</summary>
    [JSExport]
    public static double[] TileVectors()
    {
        var v = Current.Map.DataStore.GetTileVectors();
        var result = new double[v.Length * 3];
        for (int i = 0; i < v.Length; i++)
        {
            result[i * 3] = v[i].X;
            result[i * 3 + 1] = v[i].Y;
            result[i * 3 + 2] = v[i].Z;
        }
        return result;
    }

    /// <summary>Icosphere triangles over tile indices, three per face.</summary>
    [JSExport]
    public static int[] Faces()
    {
        var f = _faces ?? throw new InvalidOperationException("Generate first.");
        var result = new int[f.Length * 3];
        for (int i = 0; i < f.Length; i++)
        {
            result[i * 3] = f[i].v1;
            result[i * 3 + 1] = f[i].v2;
            result[i * 3 + 2] = f[i].v3;
        }
        return result;
    }

    /// <summary>Per-tile height; sea level is 0.</summary>
    [JSExport]
    public static double[] Heights()
    {
        var h = Current.Map.DataStore.GetSpan<ElevationInfo>();
        var result = new double[h.Length];
        for (int i = 0; i < h.Length; i++) result[i] = h[i].Height;
        return result;
    }

    /// <summary>Per-tile <see cref="BiomeType"/>.</summary>
    [JSExport]
    public static byte[] Biomes()
    {
        var b = Current.Map.DataStore.GetSpan<LocalMapInfo>();
        var result = new byte[b.Length];
        for (int i = 0; i < b.Length; i++) result[i] = (byte)b[i].Biome;
        return result;
    }

    /// <summary>Per tile: plate id (1-based) in the low 16 bits, <see cref="PlateBoundaryType"/> in bits 16-23, crust in bit 24.</summary>
    [JSExport]
    public static int[] Plates()
    {
        var p = Current.Map.DataStore.GetSpan<TectonicPlate>();
        var result = new int[p.Length];
        for (int i = 0; i < p.Length; i++)
            result[i] = p[i].Id | ((int)p[i].Boundary << 16) | ((int)p[i].Crust << 24);
        return result;
    }

    /// <summary>Per plate row: drift unit vector xyz and speed, four doubles per plate.</summary>
    [JSExport]
    public static double[] PlateDrift()
    {
        var store = Current.Map.DataStore;
        var p = store.GetSpan<TectonicPlate>();
        int count = 0;
        for (int i = 0; i < p.Length; i++) count = Math.Max(count, p[i].Id);
        var table = store.GetTable<PlateInfo>();
        var result = new double[count * 4];
        for (int i = 0; i < count && i < table.Length; i++)
        {
            result[i * 4] = table[i].DriftX;
            result[i * 4 + 1] = table[i].DriftY;
            result[i * 4 + 2] = table[i].DriftZ;
            result[i * 4 + 3] = table[i].DriftSpeed;
        }
        return result;
    }

    /// <summary>Per-tile river discharge (drives river width).</summary>
    [JSExport]
    public static double[] Flow()
    {
        var h = Current.Map.DataStore.GetSpan<HydrologyInfo>();
        var result = new double[h.Length];
        for (int i = 0; i < h.Length; i++) result[i] = h[i].Flow;
        return result;
    }

    /// <summary><c>[count, (id, terminal, terminalBody, n, tiles...)...]</c>, downstream order.</summary>
    [JSExport]
    public static int[] Rivers()
    {
        var list = new List<int> { Current.Rivers.Rivers.Count };
        foreach (var r in Current.Rivers.Rivers)
        {
            list.Add(r.Id); list.Add((int)r.Terminal); list.Add(r.TerminalBody); list.Add(r.Path.Count);
            list.AddRange(r.Path);
        }
        return list.ToArray();
    }

    /// <summary><c>[count, (id, kind, n, tiles...)...]</c>; kind is <see cref="WaterBodyKind"/>.</summary>
    [JSExport]
    public static int[] WaterBodies()
    {
        var list = new List<int> { Current.WaterBodies.Bodies.Count };
        foreach (var b in Current.WaterBodies.Bodies)
        {
            list.Add(b.Id); list.Add((int)b.Kind); list.Add(b.Tiles.Count);
            list.AddRange(b.Tiles);
        }
        return list.ToArray();
    }

    /// <summary><c>[count, (id, kind, n, tiles...)...]</c>; kind is <see cref="RangeKind"/>.</summary>
    [JSExport]
    public static int[] Ranges()
    {
        var list = new List<int> { Current.Ranges.Features.Count };
        foreach (var r in Current.Ranges.Features)
        {
            list.Add(r.Id); list.Add((int)r.Kind); list.Add(r.Tiles.Count);
            list.AddRange(r.Tiles);
        }
        return list.ToArray();
    }

    /// <summary><c>[count, (id, type, tile, richness*1000)...]</c>; type is <see cref="DepositType"/>.</summary>
    [JSExport]
    public static int[] Deposits()
    {
        var d = Current.Deposits.Deposits;
        var result = new int[1 + d.Count * 4];
        result[0] = d.Count;
        for (int i = 0; i < d.Count; i++)
        {
            result[1 + i * 4] = d[i].Id;
            result[2 + i * 4] = (int)d[i].Type;
            result[3 + i * 4] = d[i].TileIndex;
            result[4 + i * 4] = (int)Math.Round(d[i].Richness * 1000);
        }
        return result;
    }

    /// <summary>Per tile temperature and precipitation (both ~[0, 1]), interleaved.</summary>
    [JSExport]
    public static double[] Climate()
    {
        var c = Current.Map.DataStore.GetSpan<ClimateInfo>();
        var result = new double[c.Length * 2];
        for (int i = 0; i < c.Length; i++)
        {
            result[i * 2] = c[i].Temperature;
            result[i * 2 + 1] = c[i].Precipitation;
        }
        return result;
    }

    /// <summary>
    /// The whole climate layer, twelve doubles per tile: temperature, precipitation, wind x y z, seasonal range,
    /// rain seasonality, ocean heat, current x y z, rain phase (<see cref="ClimateInfo"/>). Wind and current are 3D tangent
    /// vectors; the host projects them onto east/north with its own tile vectors.
    /// </summary>
    [JSExport]
    public static double[] ClimateField()
    {
        var c = Current.Map.DataStore.GetSpan<ClimateInfo>();
        var result = new double[c.Length * 12];
        for (int i = 0; i < c.Length; i++)
        {
            int o = i * 12;
            result[o] = c[i].Temperature; result[o + 1] = c[i].Precipitation;
            result[o + 2] = c[i].WindX; result[o + 3] = c[i].WindY; result[o + 4] = c[i].WindZ;
            result[o + 5] = c[i].SeasonalRange; result[o + 6] = c[i].RainSeasonality; result[o + 7] = c[i].OceanHeat;
            result[o + 8] = c[i].CurrentX; result[o + 9] = c[i].CurrentY; result[o + 10] = c[i].CurrentZ;
            result[o + 11] = c[i].RainPhase;
        }
        return result;
    }

    /// <summary>
    /// What each tile could carry, five bytes per tile interleaved: arable, pasture, game, timber, fish
    /// (<see cref="LandCapacityInfo"/>).
    /// </summary>
    [JSExport]
    public static byte[] LandCapacity()
    {
        var c = Current.Map.DataStore.GetSpan<LandCapacityInfo>();
        var result = new byte[c.Length * 5];
        for (int i = 0; i < c.Length; i++)
        {
            result[i * 5] = c[i].Arable; result[i * 5 + 1] = c[i].Pasture; result[i * 5 + 2] = c[i].Game;
            result[i * 5 + 3] = c[i].Timber; result[i * 5 + 4] = c[i].Fish;
        }
        return result;
    }

    /// <summary>Sea ice per tile, one byte each: 0 open water or land, 1 seasonal, 2 perennial (<see cref="SeaIceKind"/>).</summary>
    [JSExport]
    public static byte[] SeaIce()
    {
        var mask = RoguelikeToolkit.World.Core.Climate.SeaIce.Mask(Current.Map.DataStore);
        var result = new byte[mask.Length];
        for (int i = 0; i < mask.Length; i++) result[i] = (byte)mask[i];
        return result;
    }

    /// <summary>What is decided for each tile, three bytes per tile: use, activities, intensity (<see cref="LandUseInfo"/>). Only hand pins unless the host adds allocation stages.</summary>
    [JSExport]
    public static byte[] LandUse()
    {
        var u = Current.Map.DataStore.GetSpan<LandUseInfo>();
        var result = new byte[u.Length * 3];
        for (int i = 0; i < u.Length; i++)
        {
            result[i * 3] = u[i].Use; result[i * 3 + 1] = u[i].Activities; result[i * 3 + 2] = u[i].Intensity;
        }
        return result;
    }

    // The editor's one site search: both exports below must ask the same question or their arrays stop lining up.
    private static List<CitySiteScore> SiteSearch(int max)
        => Current.ScoreCitySites(new CitySiteFilter { TopN = max, FreshWaterRadiusKm = 400 });

    /// <summary>
    /// Every viable settlement tile with its score, best first: <c>[tile, score*1000]</c> pairs.
    /// Spacing and the city/town/village hierarchy are a cartographic decision, made by the editor.
    /// </summary>
    [JSExport]
    public static int[] CitySites(int max)
    {
        var sites = SiteSearch(max);
        var result = new int[sites.Count * 2];
        for (int i = 0; i < sites.Count; i++)
        {
            result[i * 2] = sites[i].TileIndex;
            result[i * 2 + 1] = (int)Math.Round(sites[i].Score * 1000);
        }
        return result;
    }

    /// <summary>
    /// Why each site scored what it did: one comma-joined list of <see cref="SettlementScoring"/> terms per site (<c>"water,fertile"</c>),
    /// in the same order as the pairs <see cref="CitySites"/> returns, so entry <c>i</c> explains pair <c>i</c>. The search runs again
    /// rather than being cached (no mutable statics); it is a single pass over the land tiles.
    /// </summary>
    [JSExport]
    public static string[] CitySiteReasons(int max)
    {
        var sites = SiteSearch(max);
        var result = new string[sites.Count];
        for (int i = 0; i < sites.Count; i++) result[i] = sites[i].Reasons;
        return result;
    }

    /// <summary>Name styles as <c>race\tculture\tlabel</c>, UI order.</summary>
    [JSExport]
    public static string[] NameStyles()
        => FantasyNameGenerator.Styles().Select(s => $"{s.Race}\t{s.Culture}\t{s.Label}").ToArray();

    /// <summary>
    /// Batch place naming: one interop hop for the whole map. <paramref name="context"/> bits:
    /// 1 near water, 2 near forest, 4 dark forest, 8 highland, 16 valley, 32 deposit, 64 dangerous.
    /// Latin script: the map fonts are Latin, and labels must render the same on screen and in SVG.
    /// </summary>
    [JSExport]
    public static string[] NamePlaces(string[] kinds, int[] seeds, string[] races, string[] cultures, int[] context)
    {
        var result = new string[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            int c = context[i];
            var ctx = new PlaceContext
            {
                Kind = kinds[i],
                NearWater = (c & 1) != 0, NearForest = (c & 2) != 0, DarkForest = (c & 4) != 0,
                Highland = (c & 8) != 0, Valley = (c & 16) != 0, HasDeposit = (c & 32) != 0, Dangerous = (c & 64) != 0,
            };
            try
            {
                result[i] = FantasyNameGenerator.GeneratePlace(kinds[i], ctx, races[i], cultures[i], seeds[i], script: NameScript.Latin).Text;
            }
            catch (Exception e) when (e is ArgumentException or KeyNotFoundException or InvalidOperationException)
            {
                // An unknown kind/style from a content pack must not take the whole batch down.
                result[i] = string.Empty;
            }
        }
        return result;
    }

    private static CoreWorld Current => _world ?? throw new InvalidOperationException("Generate first.");
}
