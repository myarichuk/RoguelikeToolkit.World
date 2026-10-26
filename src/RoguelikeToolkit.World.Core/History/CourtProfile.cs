using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The court data (<c>Peoples/Data/Court/default.yaml</c>, <c>Peoples/Data/Traits/epithets.yaml</c>) compiled for the
/// simulation: offices per government form with their person kinds, linear skill formulas, and epithet predicates. Names the
/// history cannot place (an unknown form, person kind, trait or skill) are listed in <see cref="UnknownKeys"/> and skipped.
/// Personality and skills are read on a 0-100 scale; <see cref="ActorTraits"/> bytes are 0-255.
/// </summary>
internal sealed class CourtProfile
{
    /// <summary>Order of the predicate and formula dimensions: the five traits, then the four skills.</summary>
    internal static readonly string[] Dimensions =
    {
        "Openness", "Conscientiousness", "Extraversion", "Agreeableness", "Neuroticism", "Martial", "Intrigue", "Stewardship", "Charisma",
    };

    private const int Traits = 5;
    private const int Skills = 4;
    private const int Roll = Traits;        // formula slot of the uniform draw
    private const int Offset = Traits + 1;  // formula slot of the constant

    /// <summary>One office: its name, the kind of person who holds it, and the skill (0-3) a candidate is judged by.</summary>
    internal readonly record struct Office(string Name, PersonKind Kind, int Skill);

    private readonly Dictionary<PolityForm, Office[]> _offices = new();
    private readonly double[][] _skills = new double[Skills][];
    private readonly List<(string Key, double[] Min, double[] Max)> _epithets = new();

    private CourtProfile(CourtDef court) => Data = court;

    /// <summary>The court tuning (hazard, ages, thresholds).</summary>
    public CourtDef Data { get; }

    /// <summary>Data names the history could not place.</summary>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    /// <summary>Builds the profile from <paramref name="store"/>'s <c>default</c> court and <c>epithets</c> table; built-in defaults (no offices, no epithets) when absent.</summary>
    public static CourtProfile From(PeoplesStore? store)
    {
        CourtDef? court = null;
        store?.Courts.TryGetValue("default", out court);
        var profile = new CourtProfile(court ?? new CourtDef());
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        for (int s = 0; s < Skills; s++) profile._skills[s] = new double[Offset + 1];
        foreach (var skill in profile.Data.Skills)
        {
            int s = Array.FindIndex(Dimensions, d => string.Equals(d, skill.Key, StringComparison.OrdinalIgnoreCase)) - Traits;
            if (s < 0) { unknown.Add($"skill:{skill.Key}"); continue; }
            foreach (var w in skill.Value)
            {
                int slot = string.Equals(w.Key, "Roll", StringComparison.OrdinalIgnoreCase) ? Roll
                    : string.Equals(w.Key, "Offset", StringComparison.OrdinalIgnoreCase) ? Offset
                    : Array.FindIndex(Dimensions, 0, Traits, d => string.Equals(d, w.Key, StringComparison.OrdinalIgnoreCase));
                if (slot < 0) unknown.Add($"skill:{skill.Key}.{w.Key}");
                else profile._skills[s][slot] = w.Value;
            }
        }
        foreach (var form in profile.Data.Forms)
        {
            if (!Enum.TryParse(form.Key, ignoreCase: true, out PolityForm parsed) || !Enum.IsDefined(typeof(PolityForm), parsed))
            {
                unknown.Add($"form:{form.Key}");
                continue;
            }
            var offices = new List<Office>();
            foreach (var o in form.Value)
            {
                bool kindOk = Enum.TryParse(o.Kind, ignoreCase: true, out PersonKind kind) && Enum.IsDefined(typeof(PersonKind), kind);
                int skill = Array.FindIndex(Dimensions, d => string.Equals(d, o.Skill, StringComparison.OrdinalIgnoreCase)) - Traits;
                if (!kindOk) unknown.Add($"kind:{o.Kind}");
                if (skill < 0) unknown.Add($"skill:{o.Skill}");
                if (kindOk && skill >= 0 && kind != PersonKind.Ruler) offices.Add(new Office(o.Office, kind, skill));
            }
            profile._offices[parsed] = offices.ToArray();
        }
        TraitTableDef? table = null;
        store?.TraitTables.TryGetValue("epithets", out table);
        foreach (var e in table?.Epithets ?? new List<EpithetDef>())
        {
            var min = Bounds(e.Min, double.NegativeInfinity, e.Key, unknown);
            var max = Bounds(e.Max, double.PositiveInfinity, e.Key, unknown);
            profile._epithets.Add((e.Key, min, max));
        }
        profile.UnknownKeys = unknown.ToList();
        return profile;
    }

    private static double[] Bounds(Dictionary<string, double>? bounds, double none, string epithet, SortedSet<string> unknown)
    {
        var b = new double[Dimensions.Length];
        for (int i = 0; i < b.Length; i++) b[i] = none;
        foreach (var kv in bounds ?? new Dictionary<string, double>())
        {
            int d = Array.FindIndex(Dimensions, x => string.Equals(x, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (d < 0) unknown.Add($"epithet:{epithet}.{kv.Key}");
            else b[d] = kv.Value;
        }
        return b;
    }

    /// <summary>The offices a polity of <paramref name="form"/> keeps, in data order.</summary>
    public IReadOnlyList<Office> OfficesOf(PolityForm form) => _offices.TryGetValue(form, out var o) ? o : Array.Empty<Office>();

    /// <summary>A trait byte (0-255) on the 0-100 scale.</summary>
    public static double Scale(byte trait) => trait * (100.0 / 255.0);

    /// <summary>
    /// Rolls a personality against the folk's priors (<see cref="TraitWeight"/>, O C E A N order) and shifts it by the culture's
    /// offsets (0-100 points), drawing from <paramref name="rng"/>.
    /// </summary>
    public static ActorTraits RollTraits(ref Rng rng, TraitWeight[] priors, CultureDef? culture)
    {
        var offsets = culture == null ? new int[5] : new[]
        {
            culture.OpennessOffset, culture.ConscientiousnessOffset, culture.ExtraversionOffset, culture.AgreeablenessOffset, culture.NeuroticismOffset,
        };
        var values = new byte[5];
        for (int i = 0; i < 5; i++)
        {
            uint raw = (i < priors.Length ? priors[i] : TraitWeight.Balanced) switch
            {
                TraitWeight.Low => rng.NextUInt(128),
                TraitWeight.High => 128 + rng.NextUInt(128),
                _ => rng.NextUInt(256),
            };
            values[i] = (byte)Math.Max(0, Math.Min(255, (int)raw + (int)Math.Round(offsets[i] * 2.55)));
        }
        return new ActorTraits(values[0], values[1], values[2], values[3], values[4]);
    }

    /// <summary>The four skills (Martial, Intrigue, Stewardship, Charisma; 0-100) for <paramref name="traits"/>, one roll each.</summary>
    public byte[] RollSkills(ref Rng rng, ActorTraits traits)
    {
        var t = TraitsOf(traits);
        var skills = new byte[Skills];
        for (int s = 0; s < Skills; s++)
        {
            var w = _skills[s];
            double v = w[Offset] + w[Roll] * rng.NextDouble() * 100;
            for (int i = 0; i < Traits; i++) v += w[i] * t[i];
            skills[s] = (byte)Math.Max(0, Math.Min(100, (int)Math.Round(v)));
        }
        return skills;
    }

    private static double[] TraitsOf(ActorTraits traits) => new[]
    {
        Scale(traits.Openness), Scale(traits.Conscientiousness), Scale(traits.Extraversion), Scale(traits.Agreeableness), Scale(traits.Neuroticism),
    };

    /// <summary>Up to <see cref="CourtDef.MaxEpithets"/> epithets the person earns, drawn among those whose bounds hold, in data order.</summary>
    public List<string> Epithets(ref Rng rng, ActorTraits traits, byte[] skills)
    {
        var t = TraitsOf(traits);
        var values = new double[Dimensions.Length];
        for (int i = 0; i < Traits; i++) values[i] = t[i];
        for (int s = 0; s < Skills; s++) values[Traits + s] = skills[s];
        var earned = new List<string>();
        foreach (var (key, min, max) in _epithets)
        {
            bool holds = true;
            for (int d = 0; d < values.Length && holds; d++) holds = values[d] >= min[d] && values[d] <= max[d];
            if (holds) earned.Add(key);
        }
        // A partial Fisher-Yates over the earned list keeps the draw a pure function of the stream.
        int keep = Math.Min(Math.Max(0, Data.MaxEpithets), earned.Count);
        for (int i = 0; i < keep; i++)
        {
            int j = i + (int)rng.NextUInt((uint)(earned.Count - i));
            (earned[i], earned[j]) = (earned[j], earned[i]);
        }
        earned.RemoveRange(keep, earned.Count - keep);
        return earned;
    }

    /// <summary>Chance to die over a turn of <paramref name="turnYears"/> at <paramref name="ageYears"/> (Gompertz, mid-turn age).</summary>
    public double DeathChance(double ageYears, int lifespanYears, int turnYears)
    {
        double rate = Data.HazardBase * DetMath.Exp(Data.HazardGrowth * (ageYears + turnYears / 2.0) / Math.Max(1, lifespanYears));
        return 1 - DetMath.Exp(-turnYears * rate);
    }

    /// <summary>
    /// Rolls a person keyed by <paramref name="key"/> (a <see cref="ProposalSink.StableKey"/>): traits from the folk's priors and the
    /// culture, sex, skills and epithets, in that order from one <c>person</c> stream. Not yet proposed.
    /// </summary>
    internal Proposal.AddPerson RollPerson(ProposalSink sink, PeoplesStore? store, int key, int entity, string folkKey, string cultureKey, int seat,
        PersonKind kind, int bornAbs, int house, int parent, int years)
    {
        var rng = sink.Rng(key, "person");
        FolkDef? folk = null;
        CultureDef? culture = null;
        store?.Folks.TryGetValue(folkKey, out folk);
        store?.Cultures.TryGetValue(cultureKey, out culture);
        var priors = folk?.ToTraitWeights() ?? new[] { TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced };
        var traits = RollTraits(ref rng, priors, culture);
        bool feminine = rng.NextDouble() < Data.FeminineShare;
        var skills = RollSkills(ref rng, traits);
        var epithets = Epithets(ref rng, traits, skills);
        int bornTurn = (int)Math.Floor(bornAbs / (double)years);
        return new Proposal.AddPerson
        {
            EntityId = entity, PersonKind = kind, FolkKey = folkKey, CultureKey = cultureKey, SeatSettlement = seat,
            HouseId = house, Parent = parent, Feminine = feminine, BornTurn = bornTurn, BornYear = bornAbs - bornTurn * years,
            Traits = traits, Martial = skills[0], Intrigue = skills[1], Stewardship = skills[2], Charisma = skills[3], Epithets = epithets,
            StableKey = key,
        };
    }
}
