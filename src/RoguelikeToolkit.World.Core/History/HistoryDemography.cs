using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Shared people-and-time math for the P2b conflict operators: carrying capacity, logistic
/// growth, crowding pressure, folk generation length, and the tag shapes the operators log with.
/// Pure functions of their inputs (plus <see cref="HistoryRng"/>-style deterministic draws in the
/// stages themselves), so every operator recomputes the same trajectory for the same polity.
/// Population is an abstract index (people-equivalents, never a census); game tuning of the
/// rates is an open plan question.
/// </summary>
public static class HistoryDemography
{
    /// <summary>Share of carrying capacity a polity starts with at founding.</summary>
    public const double InitialShare = 0.1;

    /// <summary>Floor under a living polity's population index.</summary>
    public const double MinPopulation = 1.0;

    /// <summary>Population share of capacity where crowding pressure starts rising.</summary>
    public const double CrowdingStart = 0.7;

    /// <summary>Ceiling of the crowding-pressure scale (war and road-cut bumps stack up to here).</summary>
    public const double MaxPressure = 2.0;

    /// <summary>Pressure added to both sides of a war (roads cut in war pressure both sides).</summary>
    public const double WarPressureBump = 0.15;

    /// <summary>Population advantage an attacker needs to impose a culture overlay.</summary>
    public const double ConquestAdvantage = 1.5;

    /// <summary>Bounds of accumulated openness drift from exchange (+/-).</summary>
    public const int MaxOpennessDrift = 20;

    /// <summary>Exponent of the generation-length power law (fit: human ~3 turns, dwarf ~8, elf ~30 at a 10-year turn).</summary>
    public const double GenerationExponent = 0.665;

    /// <summary>Scale of the generation-length power law, in years.</summary>
    public const double GenerationScaleYears = 1.58;

    /// <summary>One folk's growth parameters: the annual logistic rate plus the lifespan figures generations derive from.</summary>
    /// <param name="GrowthR">Annual intrinsic growth rate (high-<c>r</c> litters vs low-<c>r</c> elves).</param>
    /// <param name="LifespanYears">Lifespan in years.</param>
    /// <param name="MaturationYears">Age of adulthood in years.</param>
    public readonly record struct FolkParams(double GrowthR, int LifespanYears, int MaturationYears);

    /// <summary>
    /// Resolves <paramref name="folkKey"/> to growth parameters: the shipped table when
    /// <paramref name="store"/> knows the key, else the built-in fallback for the two shipped folks,
    /// else a human-like default. Never throws for an unknown key (history simulates with data, not names).
    /// </summary>
    /// <param name="folkKey">Folk key (e.g. <c>human</c>); blank means the default.</param>
    /// <param name="store">Folk tables, or null to use fallbacks only.</param>
    public static FolkParams ResolveFolk(string folkKey, PeoplesStore? store)
    {
        if (!string.IsNullOrWhiteSpace(folkKey) && store != null
            && store.Folks.TryGetValue(folkKey, out var def))
        {
            return new FolkParams(def.GrowthR, def.LifespanYears, def.MaturationYears);
        }
        if (string.Equals(folkKey, "dwarf", StringComparison.OrdinalIgnoreCase))
            return new FolkParams(0.02, 350, 50);
        if (string.Equals(folkKey, "human", StringComparison.OrdinalIgnoreCase))
            return new FolkParams(0.05, 80, 16);
        return new FolkParams(0.03, 80, 16);
    }

    /// <summary>Compounds an annual rate over a turn: <c>1 - (1 - r)^turnYears</c>.</summary>
    /// <param name="annualR">Annual intrinsic rate, 0..10.</param>
    /// <param name="turnYears">Years per turn; must be positive.</param>
    public static double AnnualToTurnRate(double annualR, int turnYears)
    {
        if (turnYears <= 0) throw new ArgumentOutOfRangeException(nameof(turnYears), "Turn years must be positive.");
        if (annualR <= 0) return 0;
        return 1.0 - DetMath.Pow(1.0 - Math.Min(annualR, 1.0), turnYears);
    }

    /// <summary>One logistic step: <c>p + R·p·(1 - p/K)</c>, floored at <see cref="MinPopulation"/>.</summary>
    /// <param name="population">Current population index.</param>
    /// <param name="turnRate">Per-turn intrinsic rate (see <see cref="AnnualToTurnRate"/>).</param>
    /// <param name="capacity">Carrying capacity; non-positive capacity holds the floor.</param>
    public static double StepPopulation(double population, double turnRate, double capacity)
    {
        if (capacity <= 0 || turnRate <= 0) return Math.Max(population, MinPopulation);
        double next = population + turnRate * population * (1.0 - population / capacity);
        return Math.Max(next, MinPopulation);
    }

    /// <summary>
    /// Crowding pressure of a population against its capacity: 0 below
    /// <see cref="CrowdingStart"/> of capacity, rising linearly to 1 at capacity, capped at
    /// <see cref="MaxPressure"/>.
    /// </summary>
    public static double PressureOf(double population, double capacity)
    {
        if (capacity <= 0) return 0;
        double share = population / capacity;
        double pressure = (share - CrowdingStart) / (1.0 - CrowdingStart);
        return Math.Clamp(pressure, 0.0, MaxPressure);
    }

    /// <summary>
    /// Population after <paramref name="turn"/> logistic steps from the founding share
    /// (<see cref="InitialShare"/> of <paramref name="capacity"/>).
    /// </summary>
    public static double PopulationAtTurn(double capacity, double turnRate, int turn)
    {
        double population = Math.Max(capacity * InitialShare, MinPopulation);
        for (int t = 1; t <= turn; t++) population = StepPopulation(population, turnRate, capacity);
        return population;
    }

    /// <summary>Pressure after <paramref name="turn"/> logistic steps (see <see cref="PopulationAtTurn"/>).</summary>
    public static double PressureAtTurn(double capacity, double turnRate, int turn)
        => PressureOf(PopulationAtTurn(capacity, turnRate, turn), capacity);

    /// <summary>
    /// Turns per ruling generation for a folk: a power law of lifespan over the turn length
    /// (human ~3, dwarf ~8 at a 10-year turn; longer-lived folks scale up from there), at least 1.
    /// </summary>
    /// <param name="lifespanYears">Folk lifespan in years; must be positive.</param>
    /// <param name="turnYears">Years per turn; must be positive.</param>
    public static int GenerationTurns(int lifespanYears, int turnYears)
    {
        if (lifespanYears <= 0) throw new ArgumentOutOfRangeException(nameof(lifespanYears), "Lifespan must be positive.");
        if (turnYears <= 0) throw new ArgumentOutOfRangeException(nameof(turnYears), "Turn years must be positive.");
        double years = GenerationScaleYears * DetMath.Pow(lifespanYears, GenerationExponent);
        return Math.Max(1, (int)(years / turnYears + 0.5));
    }

    /// <summary>
    /// Successions ticked before <paramref name="turn"/> for a folk: one per generation boundary
    /// (<see cref="GenerationTurns"/>) strictly below <paramref name="turn"/>.
    /// </summary>
    /// <param name="lifespanYears">Folk lifespan in years; must be positive.</param>
    /// <param name="turnYears">Years per turn; must be positive.</param>
    /// <param name="turn">Turn to count successions before; must be non-negative.</param>
    public static int SuccessionsBefore(int lifespanYears, int turnYears, int turn)
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Turn cannot be negative.");
        int generation = GenerationTurns(lifespanYears, turnYears);
        int successions = 0;
        for (int t = generation; t < turn; t += generation) successions++;
        return successions;
    }

    /// <summary>
    /// War pressure <paramref name="polityId"/> had accumulated by <paramref name="turn"/>: one
    /// <see cref="WarPressureBump"/> per war it attacked or defended with a turn at or before
    /// <paramref name="turn"/>. Lets later operators (conquest, rebellion) read causal pressure
    /// without depending on stage execution order.
    /// </summary>
    /// <param name="events">Catalog event log, in log order.</param>
    /// <param name="polityId">Polity to total bumps for.</param>
    /// <param name="turn">Turn to accumulate through.</param>
    public static double EventPressureBonus(IReadOnlyList<HistoryEvent> events, int polityId, int turn)
    {
        ArgumentNullException.ThrowIfNull(events);
        double bonus = 0;
        foreach (var entry in events)
        {
            if (entry.Kind != HistoryEventKind.War || entry.Turn > turn) continue;
            if (entry.PolityId == polityId) { bonus += WarPressureBump; continue; }
            foreach (string tag in entry.Tags)
            {
                if (tag.StartsWith("defender:", StringComparison.Ordinal)
                    && int.TryParse(tag.Substring(9), out int defender)
                    && defender == polityId)
                {
                    bonus += WarPressureBump;
                    break;
                }
            }
        }
        return bonus;
    }

    /// <summary>Machine-readable route tag shared by road, road-cut and exchange events.</summary>
    /// <param name="endpointA">Smaller capital tile.</param>
    /// <param name="endpointB">Larger capital tile.</param>
    public static string RouteTag(int endpointA, int endpointB) => $"route:{endpointA}-{endpointB}";

    /// <summary>Sums a capacity tile to one carrying-capacity number (arable + pasture + game + timber).</summary>
    public static int CapacitySum(int arable, int pasture, int game, int timber) => arable + pasture + game + timber;

    /// <summary>
    /// Carrying capacity per polity, aligned with <paramref name="polities"/> by index (a polity's
    /// <c>Id</c> is its index): the capacity sum over its owned tiles, or the biome-surplus sum
    /// when no land-capacity layer is registered, or zero when neither layer exists. Tiles owned
    /// by no known polity are ignored. Tile order is ascending, so the sums are deterministic.
    /// </summary>
    /// <param name="store">Tile store holding the optional capacity/biome layers.</param>
    /// <param name="polities">Polities to total capacity for.</param>
    /// <param name="ownership">Owner polity id per tile (see <see cref="HistoryCatalog.Ownership"/>).</param>
    public static double[] CarryingCapacities(
        WorldDataStore store, IReadOnlyList<Polity> polities, int[] ownership)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(polities);
        ArgumentNullException.ThrowIfNull(ownership);
        var capacities = new double[polities.Count];
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        bool hasBiome = !hasCapacity && store.IsLayerRegistered<BiomeInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        int tiles = Math.Min(store.TileCount, ownership.Length);
        for (int tile = 0; tile < tiles; tile++)
        {
            int owner = ownership[tile];
            if ((uint)owner >= (uint)capacities.Length) continue;
            int sum;
            if (hasCapacity)
            {
                var cell = capacity[tile];
                sum = CapacitySum(cell.Arable, cell.Pasture, cell.Game, cell.Timber);
            }
            else if (hasBiome)
            {
                var profile = BiomeSurplus(biomes[tile].Biome);
                sum = CapacitySum(profile[0], profile[1], profile[2], profile[3]);
            }
            else
            {
                continue;
            }
            capacities[owner] += sum;
        }
        return capacities;
    }

    /// <summary>
    /// Fallback surplus profile for a biome, used when no land-capacity layer is registered
    /// (capacity is a land-use layer; default worlds have none). Coarse on purpose: it only has to
    /// separate grain land (plains, savanna, mediterranean) from herd/timber land (steppe-cold,
    /// forest, jungle) so complementary pairs still find each other. Layout is
    /// <c>[arable, pasture, game, timber]</c> (see <see cref="AreComplementary"/>).
    /// </summary>
    /// <param name="biome">Biome of the tile.</param>
    public static int[] BiomeSurplus(BiomeType biome) => biome switch
    {
        BiomeType.Plains => new[] { 140, 100, 40, 20 },
        BiomeType.Savanna => new[] { 80, 140, 60, 20 },
        BiomeType.Mediterranean => new[] { 130, 80, 30, 40 },
        BiomeType.Forest => new[] { 40, 40, 90, 160 },
        BiomeType.Jungle => new[] { 60, 20, 90, 140 },
        BiomeType.Swamp => new[] { 30, 20, 60, 60 },
        BiomeType.Mountain => new[] { 10, 40, 30, 40 },
        BiomeType.Tundra => new[] { 0, 60, 40, 0 },
        BiomeType.ColdDesert => new[] { 0, 40, 20, 0 },
        BiomeType.Desert => new[] { 5, 15, 10, 0 },
        BiomeType.Badlands => new[] { 5, 20, 10, 0 },
        BiomeType.RockDesert => new[] { 5, 10, 10, 0 },
        BiomeType.Canyon => new[] { 5, 10, 15, 0 },
        _ => new[] { 0, 0, 0, 0 },
    };

    /// <summary>
    /// True when one profile runs a grain surplus and the other a herd/timber surplus: one side's
    /// arable beats its pasture + timber by a quarter while the other's pasture + timber beats its
    /// arable by a quarter (or the mirror). Null, short or empty profiles are never complementary.
    /// </summary>
    /// <param name="first">Surplus profile as <c>[arable, pasture, game, timber]</c>.</param>
    /// <param name="second">Surplus profile as <c>[arable, pasture, game, timber]</c>.</param>
    public static bool AreComplementary(int[]? first, int[]? second)
    {
        if (first is null || second is null || first.Length < 4 || second.Length < 4) return false;
        int grainA = first[0], rangeA = first[1] + first[3];
        int grainB = second[0], rangeB = second[1] + second[3];
        if (grainA + rangeA == 0 || grainB + rangeB == 0) return false;
        return (grainA >= rangeA * 1.25 && rangeB >= grainB * 1.25)
            || (rangeA >= grainA * 1.25 && grainB >= rangeB * 1.25);
    }
}
