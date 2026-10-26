using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Step costs of the territory flood, per owner: the <see cref="TerritoryDef"/> profile scaled by each owner's culture
/// (<see cref="CultureDef.TerrainAffinity"/>), so a marsh people's reach runs through marsh and a mountain people's over
/// ranges. Built from data; <see cref="Uniform"/> (every step costs 1) is the plain hop count.
/// </summary>
public sealed class TerritoryCosts
{
    private static readonly int BiomeCount = Enum.GetValues(typeof(BiomeType)).Cast<byte>().Max() + 1;

    private readonly double[] _step, _climb, _high, _river;
    private readonly double[][] _biome;
    private readonly double _highFrom;

    private TerritoryCosts(int owners, TerritoryDef? profile)
    {
        _step = new double[owners];
        _climb = new double[owners];
        _high = new double[owners];
        _river = new double[owners];
        _biome = new double[owners][];
        _highFrom = profile?.HighFrom ?? 0;
    }

    /// <summary>Every step costs 1: the plain hop count.</summary>
    public static TerritoryCosts Uniform { get; } = Build(null, Array.Empty<string>());

    /// <summary>Biome or term names in the data that name no <see cref="BiomeType"/> or cost term (left at 1).</summary>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Costs for owners 0..<paramref name="culturePerOwner"/>.Count-1 from <paramref name="store"/>'s <c>default</c> territory
    /// profile and each owner's culture. A store without the profile gives <see cref="Uniform"/> costs.
    /// </summary>
    public static TerritoryCosts FromPeoples(PeoplesStore? store, IReadOnlyList<string> culturePerOwner)
    {
        ArgumentNullException.ThrowIfNull(culturePerOwner);
        TerritoryDef? profile = null;
        store?.Territories.TryGetValue("default", out profile);
        return Build(profile == null ? null : store, culturePerOwner, profile);
    }

    private static TerritoryCosts Build(PeoplesStore? store, IReadOnlyList<string> culturePerOwner, TerritoryDef? profile = null)
    {
        int owners = culturePerOwner.Count;
        var costs = new TerritoryCosts(owners, profile);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var baseBiome = new double[BiomeCount];
        for (int b = 0; b < BiomeCount; b++) baseBiome[b] = 1;
        if (profile != null)
            foreach (var kv in profile.Biomes)
            {
                if (TryBiome(kv.Key, out int b)) baseBiome[b] = kv.Value;
                else unknown.Add(kv.Key);
            }
        var byCulture = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int o = 0; o < owners; o++)
        {
            double step = profile?.Step ?? 1, climb = profile?.Climb ?? 0, high = profile?.High ?? 0, river = profile?.River ?? 0;
            var biome = (double[])baseBiome.Clone();
            CultureDef? culture = null;
            if (profile != null && store != null && culturePerOwner[o] is { Length: > 0 } key) store.Cultures.TryGetValue(key, out culture);
            if (culture != null)
            {
                foreach (var kv in culture.TerrainAffinity)
                {
                    if (string.Equals(kv.Key, "Climb", StringComparison.OrdinalIgnoreCase)) climb *= kv.Value;
                    else if (string.Equals(kv.Key, "High", StringComparison.OrdinalIgnoreCase)) high *= kv.Value;
                    else if (string.Equals(kv.Key, "River", StringComparison.OrdinalIgnoreCase)) river *= kv.Value;
                    else if (TryBiome(kv.Key, out int b)) biome[b] *= kv.Value;
                    else unknown.Add($"{culture.Key}:{kv.Key}");
                }
            }
            costs._step[o] = step;
            costs._climb[o] = climb;
            costs._high[o] = high;
            costs._river[o] = river;
            costs._biome[o] = biome;
        }
        costs.UnknownKeys = unknown.ToList();
        return costs;
    }

    private static bool TryBiome(string name, out int biome)
    {
        biome = -1;
        if (!Enum.TryParse(name, ignoreCase: true, out BiomeType parsed) || !Enum.IsDefined(typeof(BiomeType), parsed)) return false;
        biome = (int)parsed;
        return true;
    }

    /// <summary>Cost for <paramref name="owner"/> to carry its reach from <paramref name="from"/> into <paramref name="to"/>; missing layers add nothing.</summary>
    internal double Step(int owner, int from, int to, ReadOnlySpan<ElevationInfo> elev, ReadOnlySpan<HydrologyInfo> hydro, ReadOnlySpan<BiomeInfo> biome)
    {
        if ((uint)owner >= (uint)_step.Length) return 1.0;
        double c = _step[owner];
        if (!elev.IsEmpty)
        {
            c += _climb[owner] * Math.Abs(elev[to].Height - elev[from].Height);
            double high = elev[to].Height - _highFrom;
            if (high > 0) c += _high[owner] * high;
        }
        if (!hydro.IsEmpty && hydro[to].IsRiver == 1 && hydro[from].IsRiver == 0) c += _river[owner];
        if (!biome.IsEmpty)
        {
            int b = (int)biome[to].Biome;
            if ((uint)b < (uint)_biome[owner].Length) c *= _biome[owner][b];
        }
        return c;
    }
}
