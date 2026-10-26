namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// How courts work: which offices each government form keeps, how persons age and die, how skills follow from
/// personality, and when rulers play favourites or holders feud. Data only; loaded from <c>Peoples/Data/Court/</c>, where
/// <c>default.yaml</c> is the one the history uses. Ages are shares of the folk lifespan; personality and skill thresholds
/// are on a 0-100 scale.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class CourtDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Gompertz death hazard per year at birth (the <c>a</c> of <c>a·exp(b·age/lifespan)</c>), &gt;= 0.</summary>
    public double HazardBase { get; set; } = 0.0004;
    /// <summary>Gompertz growth over a lifespan (the <c>b</c>), &gt;= 0.</summary>
    public double HazardGrowth { get; set; } = 7.0;
    /// <summary>Nobody outlives this many lifespans (&gt;= 1).</summary>
    public double HardCap { get; set; } = 1.5;
    /// <summary>Earliest age a ruler dies at when their reign ends with the generation, as a lifespan share.</summary>
    public double RulerDeathAgeMin { get; set; } = 0.55;
    /// <summary>Latest such age, as a lifespan share.</summary>
    public double RulerDeathAgeMax { get; set; } = 0.95;
    /// <summary>Youngest age an office holder is appointed at, as a lifespan share.</summary>
    public double OfficeAgeMin { get; set; } = 0.25;
    /// <summary>Oldest such age, as a lifespan share.</summary>
    public double OfficeAgeMax { get; set; } = 0.6;
    /// <summary>A death below this age (lifespan share) draws an <see cref="UntimelyDeathCauses"/> cause instead of <c>age</c>.</summary>
    public double OldAge { get; set; } = 0.7;
    /// <summary>Causes of an early death, drawn uniformly.</summary>
    public List<string> UntimelyDeathCauses { get; set; } = new();
    /// <summary>Further early-death causes only women draw (joined to <see cref="UntimelyDeathCauses"/>).</summary>
    public List<string> FeminineDeathCauses { get; set; } = new();
    /// <summary>Candidates drawn for a vacant office (&gt;= 1).</summary>
    public int Candidates { get; set; } = 3;
    /// <summary>A ruler below this agreeableness appoints a favourite (the first candidate) instead of the fittest, and becomes their patron.</summary>
    public double FavouriteBelowAgreeableness { get; set; } = 35;
    /// <summary>Two office holders both above this extraversion...</summary>
    public double RivalAboveExtraversion { get; set; } = 65;
    /// <summary>...and both below this agreeableness become rivals.</summary>
    public double RivalBelowAgreeableness { get; set; } = 40;
    /// <summary>Share of persons who are women, 0-1.</summary>
    public double FeminineShare { get; set; } = 0.5;
    /// <summary>Most epithets a person carries.</summary>
    public int MaxEpithets { get; set; } = 2;
    /// <summary>
    /// Skill formulas: skill name to weights on <c>Openness</c>, <c>Conscientiousness</c>, <c>Extraversion</c>,
    /// <c>Agreeableness</c>, <c>Neuroticism</c> (0-100 each), a uniform <c>Roll</c> (0-100) and a constant <c>Offset</c>;
    /// the result is clamped to 0-100. The history reads <c>Martial</c>, <c>Intrigue</c>, <c>Stewardship</c> and <c>Charisma</c>.
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> Skills { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Offices per government form name (<c>Kingdom</c>, <c>MerchantRepublic</c>, <c>LooseAssociation</c>, <c>Horde</c>).</summary>
    public Dictionary<string, List<CourtOfficeDef>> Forms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
