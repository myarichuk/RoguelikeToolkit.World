using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The battle data (<c>Peoples/Data/Tactics/*.yaml</c>, <c>Warfare/*.yaml</c>, <c>Battle/default.yaml</c>) compiled for
/// <see cref="BattleResolver"/>: terrain classes, tactics, beats and every culture's way of war as arrays indexed by terrain,
/// tactic and season. Names the history cannot place (a terrain class no tactic knows, a beat condition it cannot parse, a
/// form, biome or yield channel that does not exist) are listed in <see cref="UnknownKeys"/> and skipped.
/// </summary>
internal sealed class WarfareProfile
{
    /// <summary>Seasons in <see cref="HistorySeason"/> order.</summary>
    internal const int Seasons = 4;

    private static readonly string[] Channels = { "Arable", "Pasture", "Game", "Timber", "Fish" };

    /// <summary>One compiled tactic. <see cref="Fit"/> is per terrain, <see cref="Counter"/> per enemy tactic, <see cref="Season"/> per season.</summary>
    internal sealed record Tactic(string Key, string Arm, bool Initiative, double Surprise, double Cover, double[] Fit, double[] Counter,
        double[] Season, bool[] Kinds, string[] Beats);

    /// <summary>One compiled custom of war.</summary>
    internal sealed record Custom(string Key, double Pursuit, double Capture, double Morale, bool Raze, double Casualties);

    /// <summary>One compiled commander epithet: spread, death and initiative terms and a draw multiplier per tactic.</summary>
    internal sealed record Epithet(double Spread, double Death, double Initiative, double[] Tactics);

    /// <summary>One way of war: doctrine weight per tactic, units, scores, campaign weight per season, customs.</summary>
    internal sealed record Way(string Key, double[] Doctrine, IReadOnlyList<WarfareUnitDef> Units, double Mobility, double Discipline,
        double SiegeSkill, double Forage, double ColdTolerance, double Levy, double[] CampaignSeasons, IReadOnlyList<Custom> Customs,
        double Fortify = 40, FortKindDef? Fort = null)
    {
        /// <summary>True when the way keeps custom <paramref name="key"/>.</summary>
        public bool Keeps(string key)
        {
            foreach (var c in Customs) if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>What a beat condition tests.</summary>
    internal enum Test : byte
    {
        Season, Terrain, Kind, River, RiverBypassed, Initiative, Winner, Outcome, WinnerTactic, LoserTactic, WinnerArm, LoserArm, Custom,
        GeneralFell, GeneralCaptured, Declined, SiegeEnd,
    }

    /// <summary>One compiled beat condition: the test, its operand (an index or a name) and whether it is negated.</summary>
    internal readonly record struct Condition(Test Test, int Value, string Name, bool Negate);

    /// <summary>One compiled beat. A tactic-bound beat is drawn only for a winner fighting a tactic that lists it.</summary>
    internal sealed record Beat(string Key, double Weight, bool Always, bool TacticBound, Condition[] When);

    private readonly Dictionary<string, Way> _ways = new(StringComparer.OrdinalIgnoreCase);
    private readonly PeoplesStore? _store;
    private readonly int[] _biomeTerrain;
    private readonly double[] _formLevy;
    private readonly Dictionary<string, Epithet> _epithets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Custom> _customs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _aftermath;
    private Way? _fallback;

    private WarfareProfile(BattleDef data, FortificationDef forts, SiegeDef siege, PeoplesStore? store)
    {
        Data = data;
        Forts = forts;
        Siege = siege;
        _store = store;
        _biomeTerrain = new int[256];
        _formLevy = new double[Enum.GetValues(typeof(PolityForm)).Length];
        _aftermath = new string[Enum.GetValues(typeof(BattleOutcome)).Length];
    }

    /// <summary>The battle tuning.</summary>
    public BattleDef Data { get; }

    /// <summary>The wall and fort tuning.</summary>
    public FortificationDef Forts { get; }

    /// <summary>The siege tuning.</summary>
    public SiegeDef Siege { get; }

    /// <summary>Months of food the season a siege begins in adds or takes, per season.</summary>
    public double[] FoodSeason { get; } = new double[Seasons];

    /// <summary>Wall strength of a settlement of <paramref name="tier"/> before its people's appetite for walls.</summary>
    public double TierWalls(SettlementTier tier) => ByTier(Forts.TierWalls, tier);

    /// <summary>Share of a settlement of <paramref name="tier"/>'s people who hold its walls.</summary>
    public double GarrisonShare(SettlementTier tier) => ByTier(Siege.GarrisonShare, tier);

    private static double ByTier(Dictionary<string, double> table, SettlementTier tier)
        => table.TryGetValue(tier.ToString(), out double v) ? v : 0;

    /// <summary>
    /// Wall strength of a settlement of <paramref name="tier"/> held by a people of <paramref name="way"/>: tier, capital and
    /// high ground, scaled by the people's appetite for walls, plus the fort's own walls when it is a fort.
    /// </summary>
    public double Walls(SettlementTier tier, bool capital, bool high, bool fort, Way way)
    {
        var f = Forts;
        double walls = (TierWalls(tier) + (capital ? f.CapitalWalls : 0) + (high ? f.HighWalls : 0)) * (f.WallFloor + f.WallFortify * way.Fortify / 100.0);
        return walls + (fort ? way.Fort?.Walls ?? 0 : 0);
    }

    /// <summary>Terrain class names, ordinal order; a terrain index points here.</summary>
    public string[] Terrains { get; private set; } = Array.Empty<string>();

    /// <summary>Defender's bonus per terrain index.</summary>
    public double[] Defence { get; private set; } = Array.Empty<double>();

    /// <summary>Terrain index of default ground.</summary>
    public int DefaultTerrain { get; private set; }

    /// <summary>Terrain index of default ground at or above <see cref="BattleDef.HillsFrom"/>.</summary>
    public int HillsTerrain { get; private set; }

    /// <summary>Tactics, ordinal key order; a tactic index points here.</summary>
    public Tactic[] Tactics { get; private set; } = Array.Empty<Tactic>();

    /// <summary>Beats in data order (tactic beats the data does not list come last, weight 1, no conditions).</summary>
    public Beat[] Beats { get; private set; } = Array.Empty<Beat>();

    /// <summary>Supply carried along the lines per season.</summary>
    public double[] SeasonSupply { get; } = new double[Seasons];

    /// <summary>Weight of each yield channel (Arable, Pasture, Game, Timber, Fish) in what a host can forage.</summary>
    public double[] Forage { get; } = new double[5];

    /// <summary>Data names the history could not place.</summary>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    /// <summary>Share of a polity of <paramref name="form"/> under arms.</summary>
    public double FormLevy(PolityForm form) => (uint)form < (uint)_formLevy.Length ? _formLevy[(int)form] : 0;

    /// <summary>The aftermath action of <paramref name="outcome"/> (<c>capture</c>, <c>raze</c>, <c>sack</c>, <c>grudge</c>, <c>none</c>).</summary>
    public string Aftermath(BattleOutcome outcome) => (uint)outcome < (uint)_aftermath.Length ? _aftermath[(int)outcome] : "none";

    /// <summary>The battle effect of epithet <paramref name="key"/>, or null.</summary>
    public Epithet? EpithetOf(string key) => _epithets.TryGetValue(key, out var e) ? e : null;

    /// <summary>Terrain index of a tile with <paramref name="biome"/> at <paramref name="height"/>.</summary>
    public int TerrainOf(BiomeType biome, double height)
    {
        int t = _biomeTerrain[(byte)biome];
        return t == DefaultTerrain && height >= Data.HillsFrom ? HillsTerrain : t;
    }

    /// <summary>Index of tactic <paramref name="key"/>, or -1.</summary>
    public int TacticIndex(string key)
    {
        for (int i = 0; i < Tactics.Length; i++)
            if (string.Equals(Tactics[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Index of terrain class <paramref name="key"/>, or -1.</summary>
    public int TerrainIndex(string key) => Array.FindIndex(Terrains, t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The way of war of culture <paramref name="cultureKey"/>: its own file, else <c>default</c>, else every field tactic at
    /// weight 1 with middling scores.
    /// </summary>
    public Way WayOf(string cultureKey)
    {
        string key = cultureKey ?? string.Empty;
        if (_ways.TryGetValue(key, out var way)) return way;
        WarfareDef? def = null;
        if (_store != null && !_store.Warfare.TryGetValue(key, out def)) _store.Warfare.TryGetValue("default", out def);
        way = def == null ? _fallback ??= Fallback() : Compile(def, null);
        _ways[key] = way;
        return way;
    }

    /// <summary>Builds the profile from <paramref name="store"/>'s tactics, ways of war and <c>default</c> battle model.</summary>
    public static WarfareProfile From(PeoplesStore? store)
    {
        BattleDef? battle = null;
        FortificationDef? forts = null;
        SiegeDef? siege = null;
        store?.Battles.TryGetValue("default", out battle);
        store?.Fortifications.TryGetValue("default", out forts);
        store?.Sieges.TryGetValue("default", out siege);
        var profile = new WarfareProfile(battle ?? new BattleDef(), forts ?? new FortificationDef(), siege ?? new SiegeDef(), store);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        profile.Build(unknown);
        // Lint every shipped way of war now, so its unknown names surface without a battle.
        if (store != null)
            foreach (var w in store.Warfare.Values.OrderBy(w => w.Key, StringComparer.Ordinal))
                profile._ways[w.Key] = profile.Compile(w, unknown);
        profile.UnknownKeys = unknown.ToList();
        return profile;
    }

    private void Build(SortedSet<string> unknown)
    {
        var data = Data;
        var terrains = data.TerrainDefence.Count > 0
            ? data.TerrainDefence.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()
            : new[] { data.DefaultTerrain };
        Terrains = terrains;
        Defence = terrains.Select(t => data.TerrainDefence.TryGetValue(t, out double d) ? d : 0).ToArray();
        DefaultTerrain = Math.Max(0, TerrainIndex(data.DefaultTerrain));
        HillsTerrain = TerrainIndex(data.HillsTerrain) is int h and >= 0 ? h : DefaultTerrain;
        for (int b = 0; b < _biomeTerrain.Length; b++) _biomeTerrain[b] = DefaultTerrain;
        foreach (var kv in data.BiomeTerrain)
        {
            int t = TerrainIndex(kv.Value);
            if (!Enum.TryParse(kv.Key, ignoreCase: true, out BiomeType biome) || !Enum.IsDefined(typeof(BiomeType), biome)) unknown.Add($"biome:{kv.Key}");
            else if (t < 0) unknown.Add($"terrain:{kv.Value}");
            else _biomeTerrain[(byte)biome] = t;
        }
        foreach (var kv in data.FormLevy)
        {
            if (Enum.TryParse(kv.Key, ignoreCase: true, out PolityForm form) && Enum.IsDefined(typeof(PolityForm), form)) _formLevy[(int)form] = kv.Value;
            else unknown.Add($"form:{kv.Key}");
        }
        for (int s = 0; s < Seasons; s++) SeasonSupply[s] = 1;
        foreach (var kv in data.SeasonSupply)
        {
            int s = SeasonIndex(kv.Key);
            if (s < 0) unknown.Add($"season:{kv.Key}");
            else SeasonSupply[s] = kv.Value;
        }
        foreach (var kv in data.ForageChannels)
        {
            int c = Array.FindIndex(Channels, x => string.Equals(x, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (c < 0) unknown.Add($"forage:{kv.Key}");
            else Forage[c] = kv.Value;
        }
        foreach (var kv in Siege.FoodSeason)
        {
            int season = SeasonIndex(kv.Key);
            if (season < 0) unknown.Add($"season:{kv.Key}");
            else FoodSeason[season] = kv.Value;
        }
        foreach (var t in Forts.TierWalls.Keys.Concat(Siege.GarrisonShare.Keys))
            if (!Enum.TryParse(t, ignoreCase: true, out SettlementTier _)) unknown.Add($"tier:{t}");
        for (int o = 0; o < _aftermath.Length; o++) _aftermath[o] = "none";
        foreach (var kv in data.Aftermath)
        {
            if (Enum.TryParse(kv.Key, ignoreCase: true, out BattleOutcome outcome) && Enum.IsDefined(typeof(BattleOutcome), outcome))
                _aftermath[(int)outcome] = (kv.Value ?? "none").Trim().ToLowerInvariant();
            else unknown.Add($"outcome:{kv.Key}");
        }

        foreach (var kv in data.Customs)
        {
            var c = kv.Value ?? new BattleCustomDef();
            _customs[kv.Key] = new Custom(kv.Key, c.Pursuit, c.Capture, c.Morale, c.Raze, c.Casualties);
        }

        var defs = _store?.Tactics.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToList() ?? new List<TacticDef>();
        var tactics = new Tactic[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            var fit = Factors(terrains, d.TerrainFit, $"tactic:{d.Key}.terrain", unknown);
            var counter = new double[defs.Count];
            for (int j = 0; j < defs.Count; j++) counter[j] = d.Counters.TryGetValue(defs[j].Key, out double c) ? c : 1;
            var season = new double[Seasons];
            for (int s = 0; s < Seasons; s++) season[s] = 1;
            foreach (var kv in d.Seasons)
            {
                int s = SeasonIndex(kv.Key);
                if (s >= 0) season[s] = kv.Value;
            }
            var kinds = new bool[Enum.GetValues(typeof(BattleKind)).Length];
            if (d.Kinds.Count == 0) kinds[(int)BattleKind.Field] = kinds[(int)BattleKind.Ambush] = kinds[(int)BattleKind.Relief] = true;
            foreach (var k in d.Kinds)
                if (Enum.TryParse(k, ignoreCase: true, out BattleKind kind) && Enum.IsDefined(typeof(BattleKind), kind))
                {
                    kinds[(int)kind] = true;
                    if (kind == BattleKind.Field) kinds[(int)BattleKind.Ambush] = true;
                }
            tactics[i] = new Tactic(d.Key, d.Arm ?? string.Empty, d.Initiative, d.Surprise, d.Cover, fit, counter, season, kinds, d.Beats.ToArray());
        }
        Tactics = tactics;

        var bound = new HashSet<string>(tactics.SelectMany(t => t.Beats), StringComparer.OrdinalIgnoreCase);
        var beats = new List<Beat>();
        foreach (var b in data.Beats)
        {
            var when = new List<Condition>();
            foreach (var token in b.When)
            {
                if (TryCondition(token, out var condition)) when.Add(condition);
                else unknown.Add($"beat:{b.Key}.{token}");
            }
            beats.Add(new Beat(b.Key, b.Weight, b.Always, bound.Contains(b.Key), when.ToArray()));
        }
        foreach (var key in bound.OrderBy(k => k, StringComparer.Ordinal))
            if (!beats.Any(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                unknown.Add($"beat:{key}");
                beats.Add(new Beat(key, 1, false, true, Array.Empty<Condition>()));
            }
        Beats = beats.ToArray();

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_store != null && _store.TraitTables.TryGetValue("epithets", out var table))
            foreach (var e in table.Epithets) known.Add(e.Key);
        foreach (var kv in data.Epithets)
        {
            if (_store != null && !known.Contains(kv.Key)) unknown.Add($"epithet:{kv.Key}");
            var mult = new double[tactics.Length];
            for (int i = 0; i < mult.Length; i++) mult[i] = 1;
            foreach (var t in kv.Value.Tactics)
            {
                int i = TacticIndex(t.Key);
                if (i < 0) unknown.Add($"epithet:{kv.Key}.{t.Key}");
                else mult[i] = t.Value;
            }
            _epithets[kv.Key] = new Epithet(kv.Value.Spread, kv.Value.Death, kv.Value.Initiative, mult);
        }
    }

    private static double[] Factors(string[] terrains, Dictionary<string, double> values, string where, SortedSet<string> unknown)
    {
        var result = new double[terrains.Length];
        for (int i = 0; i < result.Length; i++) result[i] = 1;
        foreach (var kv in values)
        {
            int t = Array.FindIndex(terrains, x => string.Equals(x, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (t < 0) unknown.Add($"{where}:{kv.Key}");
            else result[t] = kv.Value;
        }
        return result;
    }

    /// <summary>Index of season <paramref name="name"/> in <see cref="HistorySeason"/> order, or -1.</summary>
    internal static int SeasonIndex(string name)
        => Enum.TryParse(name, ignoreCase: true, out HistorySeason s) && Enum.IsDefined(typeof(HistorySeason), s) ? (int)s : -1;

    private Way Compile(WarfareDef def, SortedSet<string>? unknown)
    {
        var doctrine = new double[Tactics.Length];
        foreach (var kv in def.Doctrine)
        {
            int i = TacticIndex(kv.Key);
            if (i < 0) unknown?.Add($"warfare:{def.Key}.{kv.Key}");
            else doctrine[i] = kv.Value;
        }
        var seasons = new double[Seasons];
        foreach (var kv in def.CampaignSeasons)
        {
            int s = SeasonIndex(kv.Key);
            if (s >= 0) seasons[s] = kv.Value;
        }
        if (seasons.Sum() <= 0) seasons[(int)HistorySeason.Summer] = 1;
        var customs = new List<Custom>();
        foreach (var c in def.Customs)
        {
            if (_customs.TryGetValue(c, out var custom)) customs.Add(custom);
            else unknown?.Add($"warfare:{def.Key}.custom:{c}");
        }
        foreach (var u in def.Units)
            if (!Tactics.Any(t => string.Equals(t.Arm, u.Arm, StringComparison.OrdinalIgnoreCase))) unknown?.Add($"warfare:{def.Key}.arm:{u.Arm}");
        if (!string.IsNullOrWhiteSpace(def.FortKind) && !Forts.Kinds.ContainsKey(def.FortKind)) unknown?.Add($"warfare:{def.Key}.fort:{def.FortKind}");
        return new Way(def.Key, doctrine, def.Units.ToArray(), def.Mobility, def.Discipline, def.SiegeSkill, def.Forage, def.ColdTolerance,
            def.Levy, seasons, customs, def.Fortify, FortOf(def.FortKind));
    }

    private Way Fallback()
    {
        var doctrine = new double[Tactics.Length];
        for (int i = 0; i < doctrine.Length; i++) doctrine[i] = Tactics[i].Kinds[(int)BattleKind.Field] ? 1 : 0;
        var seasons = new double[Seasons];
        for (int s = 0; s < Seasons; s++) seasons[s] = 1;
        return new Way("default", doctrine, Array.Empty<WarfareUnitDef>(), 50, 50, 50, 50, 50, 1, seasons, Array.Empty<Custom>(), 40, FortOf(null));
    }

    /// <summary>Fort kind <paramref name="key"/>, else the data's default kind, else null (no forts).</summary>
    private FortKindDef? FortOf(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key) && Forts.Kinds.TryGetValue(key!, out var kind)) return kind;
        return !string.IsNullOrWhiteSpace(Forts.DefaultKind) && Forts.Kinds.TryGetValue(Forts.DefaultKind, out var fallback) ? fallback : null;
    }

    private bool TryCondition(string token, out Condition condition)
    {
        condition = default;
        string t = (token ?? string.Empty).Trim();
        bool negate = t.StartsWith("!", StringComparison.Ordinal);
        if (negate) t = t.Substring(1).Trim();
        int eq = t.IndexOf('=');
        string key = (eq < 0 ? t : t.Substring(0, eq)).Trim().ToLowerInvariant();
        string value = eq < 0 ? string.Empty : t.Substring(eq + 1).Trim();
        switch (key)
        {
            case "river" when eq < 0: condition = new Condition(Test.River, 0, string.Empty, negate); return true;
            case "river_bypassed" when eq < 0: condition = new Condition(Test.RiverBypassed, 0, string.Empty, negate); return true;
            case "general_fell" when eq < 0: condition = new Condition(Test.GeneralFell, 0, string.Empty, negate); return true;
            case "general_captured" when eq < 0: condition = new Condition(Test.GeneralCaptured, 0, string.Empty, negate); return true;
            case "declined" when eq < 0: condition = new Condition(Test.Declined, 0, string.Empty, negate); return true;
            case "end":
                if (value.Length == 0) return false;
                condition = new Condition(Test.SiegeEnd, 0, value, negate);
                return true;
            case "season":
            {
                int s = SeasonIndex(value);
                if (s < 0) return false;
                condition = new Condition(Test.Season, s, value, negate);
                return true;
            }
            case "terrain":
            {
                int i = TerrainIndex(value);
                if (i < 0) return false;
                condition = new Condition(Test.Terrain, i, value, negate);
                return true;
            }
            case "kind":
                if (!Enum.TryParse(value, ignoreCase: true, out BattleKind kind) || !Enum.IsDefined(typeof(BattleKind), kind)) return false;
                condition = new Condition(Test.Kind, (int)kind, value, negate);
                return true;
            case "initiative":
            case "winner":
            {
                int side = value.ToLowerInvariant() switch { "attacker" => 0, "defender" => 1, "none" when key == "winner" => 2, _ => -1 };
                if (side < 0) return false;
                condition = new Condition(key == "winner" ? Test.Winner : Test.Initiative, side, value, negate);
                return true;
            }
            case "outcome":
            {
                int o = Array.IndexOf(OutcomeClasses, value.ToLowerInvariant());
                if (o < 0) return false;
                condition = new Condition(Test.Outcome, o, value, negate);
                return true;
            }
            case "winner_tactic":
            case "loser_tactic":
            {
                int i = TacticIndex(value);
                if (i < 0) return false;
                condition = new Condition(key == "winner_tactic" ? Test.WinnerTactic : Test.LoserTactic, i, value, negate);
                return true;
            }
            case "winner_arm":
            case "loser_arm":
                if (value.Length == 0) return false;
                condition = new Condition(key == "winner_arm" ? Test.WinnerArm : Test.LoserArm, 0, value, negate);
                return true;
            case "custom":
                if (!_customs.ContainsKey(value)) return false;
                condition = new Condition(Test.Custom, 0, value, negate);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Outcome classes of the <c>outcome=</c> condition, in <see cref="BattleResolver.OutcomeClass"/> order.</summary>
    internal static readonly string[] OutcomeClasses = { "decisive", "victory", "pyrrhic", "stalemate", "averted" };
}
