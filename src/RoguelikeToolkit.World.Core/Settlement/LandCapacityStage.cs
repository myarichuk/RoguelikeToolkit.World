using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// Fills <see cref="LandCapacityInfo"/> from the finished physics and *resets* <see cref="LandUseInfo"/> to
/// "nothing decided". It is the first writer of the land-use layer, so the pipeline's clearing rule gives
/// every allocation stage a clean slate on every run (including <see cref="World.Recompute"/>): a plugin
/// that reads and writes <see cref="LandUseInfo"/> refines what the stages before it decided, exactly like
/// erosion refines elevation. Not attribute-discovered; <see cref="WorldBuilder.WithLandUse"/> adds it.
/// <para>
/// Order 25 sits after the biome stage (20) and the 21..24 gap for biome and danger tweaks, so capacity
/// reads the biome the world finally has. Allocation plugins go at 26..98; <see cref="LandUseResolveStage"/>
/// closes the pipeline at 99.
/// </para>
/// </summary>
[Preserve]
public sealed class LandCapacityStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed
{
    public const int StageOrder = 25;

    public string Name => "LandCapacity";
    public int Order => StageOrder;
    public Type[] Reads => new[] { typeof(ElevationInfo), typeof(LocalMapInfo) };
    public Type[] ReadsOptional => new[] { typeof(ClimateInfo), typeof(HydrologyInfo) };
    public Type[] Writes => new[] { typeof(LandCapacityInfo), typeof(LandUseInfo) };

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var local = store.GetSpan<LocalMapInfo>();
        var capacity = store.GetSpan<LandCapacityInfo>();
        var use = store.GetSpan<LandUseInfo>();
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;

        var vectors = store.GetTileVectors();
        Span<int> nb = stackalloc int[6];
        for (int i = 0; i < capacity.Length; i++)
        {
            use[i] = default;

            double h = elev[i].Height;
            double slope = 0;
            bool fresh = hasHydro && IsFresh(hydro[i]);
            bool onFresh = fresh;
            bool nextToLand = false;
            int n = store.GetAdjacent(i, nb);
            for (int k = 0; k < n; k++)
            {
                int j = nb[k];
                double arc = (vectors[j] - vectors[i]).Length;
                if (arc > 0) slope = Math.Max(slope, Math.Abs(elev[j].Height - h) / arc);
                if (hasHydro && IsFresh(hydro[j])) fresh = true;
                if (elev[j].Height >= ElevationGenerationStage.SeaLevel) nextToLand = true;
            }

            capacity[i] = LandCapacity.Evaluate(new LandSite(
                local[i].Biome, h,
                hasClimate ? climate[i].Temperature : 0.5,
                hasClimate ? climate[i].Precipitation : 0.5,
                slope, fresh, onFresh,
                hasHydro && hydro[i].Saline != 0,
                hasClimate ? climate[i].OceanHeat : 0,
                nextToLand,
                hasClimate ? SeaIce.Classify(vectors[i], (float)h, climate[i].Temperature, climate[i].SeasonalRange) : SeaIceKind.None));
        }
    }

    private static bool IsFresh(in HydrologyInfo h) => h.IsRiver != 0 || h.LakeDepth > 0f;
}
