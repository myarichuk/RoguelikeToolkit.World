using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Elevation;

/// <summary>
/// Thermal (talus) + hillslope diffusion + glacial U-valley carving + fluvial
/// stream-power incision. All transport is pairwise mass-preserving (every move
/// takes from one tile and gives to another), so total mass is conserved and
/// output stays deterministic for a given input. Runs after climate (16) and
/// before hydrology extraction (18); the fluvial pass routes its own discharge
/// on the filled surface with modeled rainfall when the climate layer exists.
///
/// Process order mirrors landscape evolution: frost/talus shatters high
/// ground first, glaciers pluck valleys (overdeepening the tarns that later
/// hold meltwater lakes), then rivers incise with stream power E = K·Q^m·S^n
/// and deposit fans/deltas where power fades. Arid gorges incise narrow and
/// deep (canyons); humid ones spread their load (valleys).
/// </summary>
[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(StageOrder.Erosion, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(TectonicPlate), typeof(UserEditInfo) }, Writes = new[] { typeof(ElevationInfo) })]
public partial class ErosionGenerationStage : IWorldGeneratorStage
{
    /// <summary>When false the stage leaves elevation untouched (height pins are still restored). The individual passes are tuned by their own iteration counts.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Thermal (talus) sweeps; 0 skips only the thermal pass, the other passes are governed by their own iteration counts (use <see cref="Enabled"/> to switch the whole stage off).</summary>
    public int Iterations { get => _iterations; set => _iterations = StageKnob.NonNegative(value, nameof(Iterations)); }
    private int _iterations = 10;
    /// <summary>Angle of repose: the height drop to a neighbour a tile holds before it sheds material, in height units per tile step. Raised in orogenic rock by <see cref="HardRockTalus"/>.</summary>
    public double Talus { get => _talus; set => _talus = StageKnob.Finite(value, nameof(Talus)); }
    private double _talus = 0.03;

    /// <summary>
    /// Extra angle of repose per unit of orogeny: welded belt rock holds
    /// <c>Talus × (1 + HardRockTalus × |orogeny|)</c>.
    /// </summary>
    public double HardRockTalus { get => _hardRockTalus; set => _hardRockTalus = StageKnob.Finite(value, nameof(HardRockTalus)); }
    private double _hardRockTalus = 8.0;

    /// <summary>
    /// Erodibility divisor per unit of orogeny: welded belt rock sheds
    /// <c>Rate / (1 + HardRockResistance × |orogeny|)</c>. Repose angle alone
    /// cannot keep a range standing; against a needle-sharp uplift the excess
    /// over repose is so large that every rock type collapses at the same pace.
    /// </summary>
    public double HardRockResistance { get => _hardRockResistance; set => _hardRockResistance = StageKnob.Finite(value, nameof(HardRockResistance)); }
    private double _hardRockResistance = 4.0;
    /// <summary>Thermal shedding rate: per sweep a tile sheds <c>Rate × 0.5</c> of its steepest excess over the repose angle, scaled by erodibility and by 1.4 where the temperature is between 0.05 and 0.30 (freeze-thaw).</summary>
    public double Rate { get => _rate; set => _rate = StageKnob.Finite(value, nameof(Rate)); }
    private double _rate = 0.25;

    /// <summary>Hillslope-diffusion smoothing passes (rolling hills). 0 disables.</summary>
    public int DiffusionIterations { get => _diffusionIterations; set => _diffusionIterations = StageKnob.NonNegative(value, nameof(DiffusionIterations)); }
    private int _diffusionIterations = 2;
    /// <summary>Hillslope creep: fraction of a tile's drop to its lowest neighbour moved per diffusion pass (never more than a quarter of the drop).</summary>
    public double DiffusionRate { get => _diffusionRate; set => _diffusionRate = StageKnob.Finite(value, nameof(DiffusionRate)); }
    private double _diffusionRate = 0.03;

    /// <summary>Fluvial stream-power passes after thermal smoothing. 0 disables.</summary>
    public int HydraulicIterations { get => _hydraulicIterations; set => _hydraulicIterations = StageKnob.NonNegative(value, nameof(HydraulicIterations)); }
    private int _hydraulicIterations = 3;
    /// <summary>Scale of fluvial incision per pass: a channel tile cuts <c>HydraulicRate × power × channelisation / hardness</c> (stream power from <see cref="StreamPowerM"/> and <see cref="StreamPowerN"/>).</summary>
    public double HydraulicRate { get => _hydraulicRate; set => _hydraulicRate = StageKnob.Finite(value, nameof(HydraulicRate)); }
    private double _hydraulicRate = 0.2;

    /// <summary>Stream-power exponents: E = K·Q^m·S^n.</summary>
    public double StreamPowerM { get => _streamPowerM; set => _streamPowerM = StageKnob.Finite(value, nameof(StreamPowerM)); }
    private double _streamPowerM = 0.5;
    /// <summary>Slope exponent n of stream power E = K·Q^m·S^n (see <see cref="StreamPowerM"/> for the discharge exponent).</summary>
    public double StreamPowerN { get => _streamPowerN; set => _streamPowerN = StageKnob.Finite(value, nameof(StreamPowerN)); }
    private double _streamPowerN = 1.0;

    /// <summary>
    /// Discharge, as a fraction of the river threshold, at which a tile counts as
    /// a channel rather than a hillslope. Bedrock incision is weighted by
    /// <c>1 − exp(−Q / onset)</c>: slopes are shaped by creep (diffusion), channels by
    /// water, so the steep divides between valleys are not the fastest-eroding ground.
    /// </summary>
    public double ChannelOnset { get => _channelOnset; set => _channelOnset = StageKnob.Finite(value, nameof(ChannelOnset)); }
    private double _channelOnset = 0.12;

    /// <summary>Sediment a channel carries per unit stream power (Kc); above it the surplus is deposited.</summary>
    public double TransportCapacity { get => _transportCapacity; set => _transportCapacity = StageKnob.Finite(value, nameof(TransportCapacity)); }
    private double _transportCapacity = 2.0;

    /// <summary>Share of the over-capacity surplus dropped per tile (the rest travels on).</summary>
    public double DepositFraction { get => _depositFraction; set => _depositFraction = StageKnob.Finite(value, nameof(DepositFraction)); }
    private double _depositFraction = 0.5;

    /// <summary>Glacial carving passes between thermal and fluvial. 0 disables.</summary>
    public int GlacialIterations { get => _glacialIterations; set => _glacialIterations = StageKnob.NonNegative(value, nameof(GlacialIterations)); }
    private int _glacialIterations = 2;
    /// <summary>Strength of glacial floor abrasion and valley-wall plucking per pass, in height units.</summary>
    public double GlacialRate { get => _glacialRate; set => _glacialRate = StageKnob.Finite(value, nameof(GlacialRate)); }
    private double _glacialRate = 0.15;

    /// <summary>Incision multiplier inside canyon reaches (narrow, deep cut).</summary>
    public double CanyonBoost { get => _canyonBoost; set => _canyonBoost = StageKnob.Finite(value, nameof(CanyonBoost)); }
    private double _canyonBoost = 2.0;

    /// <summary>Must match <see cref="HydrologyStage.RiverThresholdScale"/> so canyons and rivers agree (WorldBuilder wires it).</summary>
    public float RiverThresholdScale { get => _riverThresholdScale; set => _riverThresholdScale = StageKnob.Finite(value, nameof(RiverThresholdScale)); }
    private float _riverThresholdScale = 1f;

    /// <summary>
    /// Wind-erosion passes over arid land (deflation and sand transport). 0 disables and is bit-identical to a
    /// stage without the pass. A tile here is tens to hundreds of km across, so the product is deflation
    /// basins and sand seas (a broad lowering upwind, a broad blanket downwind), never individual dunes.
    /// </summary>
    public int AeolianIterations { get => _aeolianIterations; set => _aeolianIterations = StageKnob.NonNegative(value, nameof(AeolianIterations)); }
    private int _aeolianIterations = 4;

    /// <summary>Height a fully arid, fully exposed tile yields per pass.</summary>
    public double AeolianRate { get => _aeolianRate; set => _aeolianRate = StageKnob.Finite(value, nameof(AeolianRate)); }
    private double _aeolianRate = 0.004;

    /// <summary>Most a tile may be lowered in total (the water table, a lag of pebbles, caprock stop deflation).</summary>
    public double AeolianMaxDeflation { get => _aeolianMaxDeflation; set => _aeolianMaxDeflation = StageKnob.Finite(value, nameof(AeolianMaxDeflation)); }
    private double _aeolianMaxDeflation = 0.03;

    /// <summary>Share of airborne sand a fully arid tile drops on the spot even on open ground (the rest hops on downwind).</summary>
    public double AeolianDeposit { get => _aeolianDeposit; set => _aeolianDeposit = StageKnob.Finite(value, nameof(AeolianDeposit)); }
    private double _aeolianDeposit = 0.25;

    /// <summary>Creates a stage with the default settings (10 thermal iterations); this is the constructor <see cref="WorldGenerationPipeline.Discover(string?, Action{StageDiscoveryDiagnostic}?)"/> uses.</summary>
    public ErosionGenerationStage()
    {
    }

    /// <summary>Creates a stage with the given number of thermal iterations.</summary>
    /// <param name="iterations">Value for <see cref="Iterations"/>.</param>
    public ErosionGenerationStage(int iterations = 10)
    {
        Iterations = iterations;
    }

    /// <summary>Erodes the <see cref="ElevationInfo"/> layer of <paramref name="map"/> in place: thermal, diffusion, glacial, fluvial and aeolian passes in that order, then restores any height-pinned tiles.</summary>
    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();

        Span<int> neighbors = stackalloc int[6];

        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;

        // Thermal: talus-angle collapse, boosted by frost shattering where the
        // climate hovers near freezing (periglacial freeze-thaw zone). Jacobi
        // sweeps (deltas accumulate against a frozen surface, then apply), so
        // the result cannot depend on tile order: a tile sheds toward every
        // neighbor that sits more than its angle of repose below it, in
        // proportion to the excess. The old in-place sweep handed everything
        // to the single steepest neighbor, so peaks collapsed into the foothill
        // beside them and left plateau benches instead of flanks.
        bool hasPlates = store.IsLayerRegistered<TectonicPlate>();
        var plateSpan = hasPlates ? store.GetSpan<TectonicPlate>() : default;
        var talusAt = new float[store.TileCount];
        var erodibility = new float[store.TileCount];
        for (int i = 0; i < talusAt.Length; i++)
        {
            // Bedrock stands steeper where an orogen has welded it (folded,
            // metamorphosed, cemented): ranges keep their flanks while soft
            // sediment and rift basins slump at the base angle.
            double hard = hasPlates ? Math.Min(Math.Abs(plateSpan[i].Orogeny), 1.5) : 0.0;
            talusAt[i] = (float)(Talus * (1.0 + HardRockTalus * hard));
            erodibility[i] = (float)(1.0 / (1.0 + HardRockResistance * hard));
        }
        var delta = new float[store.TileCount];
        Span<float> excess = stackalloc float[6];
        for (int iter = 0; Enabled && iter < Iterations; iter++)
        {
            Array.Clear(delta);
            for (int i = 0; i < store.TileCount; i++)
            {
                int adjacent = store.GetAdjacent(i, neighbors);
                float total = 0f, steepest = 0f;
                for (int k = 0; k < adjacent; k++)
                {
                    float e = elev[i].Height - elev[neighbors[k]].Height - talusAt[i];
                    if (e < 0f) e = 0f;
                    excess[k] = e;
                    total += e;
                    if (e > steepest) steepest = e;
                }
                if (total <= 0f) continue;

                double rate = Rate;
                if (hasClimate)
                {
                    float temp = climate[i].Temperature;
                    if (temp > 0.05f && temp < 0.30f) rate *= 1.4; // freeze-thaw
                }
                float shed = (float)(steepest * rate * 0.5) * erodibility[i];
                delta[i] -= shed;
                for (int k = 0; k < adjacent; k++)
                    if (excess[k] > 0f) delta[neighbors[k]] += shed * excess[k] / total;
            }
            for (int i = 0; i < store.TileCount; i++) elev[i].Height += delta[i];
        }

        // Diffusion: gentle creep rounding every slope (rolling hills). Skipped
        // when the stage is disabled (pristine).
        if (Enabled)
        {
            for (int d = 0; d < DiffusionIterations; d++)
            {
                for (int i = 0; i < store.TileCount; i++)
                {
                    int adjacent = store.GetAdjacent(i, neighbors);
                    int lowest = -1;
                    float best = elev[i].Height;
                    for (int k = 0; k < adjacent; k++)
                    {
                        int j = neighbors[k];
                        if (elev[j].Height < best) { best = elev[j].Height; lowest = j; }
                    }
                    if (lowest < 0) continue;
                    double slope = elev[i].Height - best;
                    if (slope <= 0) continue;
                    float move = (float)Math.Min(slope * DiffusionRate, slope * 0.25);
                    elev[i].Height -= move;
                    elev[lowest].Height += move;
                }
            }
        }

        // Glacial pass: U-valley carving with overdeepening. Skipped with
        // when the stage is disabled (pristine).
        if (Enabled && GlacialIterations > 0)
        {
            GlacialPass(store, elev, neighbors, hasClimate, climate);
        }

        // Fluvial pass: stream-power incision ordered down the filled surface. Skipped when the stage is disabled (pristine).
        if (Enabled)
        for (int h = 0; h < HydraulicIterations; h++)
        {
            FluvialPass(store, elev, neighbors, hasClimate, climate);
        }

        // Aeolian pass: wind strips loose ground in dry country and lays it down downwind. After the
        // fluvial passes so water has already taken what it can; before the pin restore so pins stay exact.
        if (Enabled && AeolianIterations > 0 && hasClimate)
            AeolianPass(store, elev, neighbors, climate, erodibility);

        // Fixed ground is restored last: erosion shapes everything around a pin (and may have moved
        // mass through it), but the pinned tile itself ends exactly where the author drew it.
        if (store.IsLayerRegistered<UserEditInfo>())
        {
            var edits = store.GetSpan<UserEditInfo>();
            for (int i = 0; i < edits.Length; i++)
                if ((edits[i].Flags & UserEditFlags.PinHeight) != 0) elev[i].Height = edits[i].PinHeight;
        }
    }
}
