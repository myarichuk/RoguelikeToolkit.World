using RoguelikeToolkit.World.Core.Actors;

namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// One folk (a biological/cosmological kind, not a culture): sapience, embodiment,
/// lifespan, growth rate, personality priors and compat group. Data only; loaded and
/// validated by <see cref="PeoplesStore"/> from <c>Peoples/Data/Folks/</c>.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class FolkDef
{
    /// <summary>Folk key, matching the file name (case-insensitive); e.g. <c>human</c>, <c>dwarf</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-readable label for pickers. Blank falls back to a title-cased Key.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Sapience: <c>full</c> (people: history actors), <c>instinct</c> (titles only, e.g. demons),
    /// or <c>setting</c> (personhood is setting-dependent, e.g. elementals). Validated at load, case-insensitive.
    /// </summary>
    public string Sapience { get; set; } = string.Empty;
    /// <summary>Body plan (humanoid, quadruped, serpentine, amorphous, incorporeal, ...); free text, required non-blank.</summary>
    public string Embodiment { get; set; } = string.Empty;
    /// <summary>Preferred habitat (temperate, mountain, aquatic, ...); free text, required non-blank.</summary>
    public string HabitatAffinity { get; set; } = string.Empty;
    /// <summary>Lifespan in years, 1-100000 (use the top for effectively ageless).</summary>
    public int LifespanYears { get; set; } = 0;
    /// <summary>Age of adulthood in years, 1-<see cref="LifespanYears"/>; feeds the actor adult gate.</summary>
    public int MaturationYears { get; set; } = 0;
    /// <summary>Intrinsic growth rate for logistic population pressure (high-<c>r</c> litters vs low-<c>r</c> elves), 0-10.</summary>
    public double GrowthR { get; set; } = 0;
    /// <summary>Compatibility family for default pair rules (mammal, fae, fiend, ...); required non-blank.</summary>
    public string CompatGroup { get; set; } = string.Empty;
    /// <summary>Openness prior: <c>low</c>, <c>balanced</c> or <c>high</c> (case-insensitive).</summary>
    public string OpennessPrior { get; set; } = string.Empty;
    /// <summary>Conscientiousness prior: <c>low</c>, <c>balanced</c> or <c>high</c>.</summary>
    public string ConscientiousnessPrior { get; set; } = string.Empty;
    /// <summary>Extraversion prior: <c>low</c>, <c>balanced</c> or <c>high</c>.</summary>
    public string ExtraversionPrior { get; set; } = string.Empty;
    /// <summary>Agreeableness prior: <c>low</c>, <c>balanced</c> or <c>high</c>.</summary>
    public string AgreeablenessPrior { get; set; } = string.Empty;
    /// <summary>Neuroticism prior: <c>low</c>, <c>balanced</c> or <c>high</c>.</summary>
    public string NeuroticismPrior { get; set; } = string.Empty;

    /// <summary>True when <see cref="Sapience"/> is full personhood (a history actor).</summary>
    public bool IsFullPerson => Sapience.Equals("full", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts the five string priors to <see cref="TraitWeight"/> values in
    /// O, C, E, A, N order, for <see cref="ActorTraits.Roll"/>.
    /// </summary>
    /// <exception cref="PeoplesDataException">A prior is not low, balanced or high.</exception>
    public TraitWeight[] ToTraitWeights() => new[]
    {
        ParsePrior(nameof(OpennessPrior), OpennessPrior),
        ParsePrior(nameof(ConscientiousnessPrior), ConscientiousnessPrior),
        ParsePrior(nameof(ExtraversionPrior), ExtraversionPrior),
        ParsePrior(nameof(AgreeablenessPrior), AgreeablenessPrior),
        ParsePrior(nameof(NeuroticismPrior), NeuroticismPrior),
    };

    private static TraitWeight ParsePrior(string field, string value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => TraitWeight.Low,
        "balanced" => TraitWeight.Balanced,
        "high" => TraitWeight.High,
        _ => throw new PeoplesDataException(new[] { $"{field}: unknown prior '{value}' (want low, balanced or high)." }),
    };
}
