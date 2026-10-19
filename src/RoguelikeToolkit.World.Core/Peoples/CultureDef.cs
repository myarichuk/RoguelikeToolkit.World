namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// One culture (learned, driftable: the "software" to a folk's "hardware"): the naming
/// table it renders through, subsistence archetype, lineage default, driftable value
/// axes, faith form, trait-expression offsets, and the mixed-ancestry naming rule.
/// Data only; loaded and validated by <see cref="PeoplesStore"/> from
/// <c>Peoples/Data/Cultures/</c>. An ethnos is a folk plus its current culture plus
/// accumulated deltas, resolved at query time.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class CultureDef
{
    /// <summary>Culture key, matching the file name (case-insensitive); e.g. <c>human_common</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-readable label for pickers. Blank falls back to a title-cased Key.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Key of the <c>CultureData</c> naming table this culture renders through.
    /// A reference, not a copy: existence is resolved late by Names (rank 5), so this
    /// rank-2 slice only checks it is non-blank.
    /// </summary>
    public string NamingTable { get; set; } = string.Empty;
    /// <summary>Default <see cref="SubsistenceDef"/> key (agrarian, pastoral, artisan, maritime, ...); must name a loaded profile.</summary>
    public string Subsistence { get; set; } = string.Empty;

    /// <summary>
    /// How readily this culture takes each government form: weights (0-100) by <c>PolityForm</c> name, e.g.
    /// <c>LooseAssociation: 30</c>. Empty means the land alone decides. Names are data here and bound to the enum by
    /// <c>History.FormAffinity</c> (Peoples sits below History).
    /// </summary>
    public Dictionary<string, int> FormAffinity { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Whose line names follow when the caller does not choose: <c>Patrilineal</c>, <c>Matrilineal</c> or <c>Bilineal</c> (validated at load, case-insensitive).</summary>
    public string LineageDefault { get; set; } = "Patrilineal";
    /// <summary>Xenophobia (parochial altruism axis), 0-100.</summary>
    public int Xenophobia { get; set; } = 0;
    /// <summary>Hierarchy claim (hierarchy/egalitarian axis), 0-100.</summary>
    public int HierarchyClaim { get; set; } = 0;
    /// <summary>Collectivism (individualism-collectivism axis), 0-100.</summary>
    public int Collectivism { get; set; } = 0;
    /// <summary>Honor (honor-face-dignity axis), 0-100.</summary>
    public int Honor { get; set; } = 0;
    /// <summary>Faith form (animism, shamanism, ancestor, polytheism, henotheism, monotheism, ...); free text, required non-blank.</summary>
    public string FaithForm { get; set; } = string.Empty;
    /// <summary>Mixed-ancestry naming strategy: <c>blend</c>, <c>dominant</c> or <c>hybrid</c> (validated at load, case-insensitive).</summary>
    public string MixedNaming { get; set; } = string.Empty;
    /// <summary>
    /// Hypodescent threshold: an ancestry share at or above it names the child after
    /// that folk; below it the <see cref="MixedNaming"/> strategy applies. In [0.5, 1].
    /// </summary>
    public double DominantShareThreshold { get; set; } = 0.75;
    /// <summary>Culture's filter on trait expression: openness offset, -100-100.</summary>
    public int OpennessOffset { get; set; } = 0;
    /// <summary>Culture's filter on trait expression: conscientiousness offset, -100-100.</summary>
    public int ConscientiousnessOffset { get; set; } = 0;
    /// <summary>Culture's filter on trait expression: extraversion offset, -100-100.</summary>
    public int ExtraversionOffset { get; set; } = 0;
    /// <summary>Culture's filter on trait expression: agreeableness offset, -100-100.</summary>
    public int AgreeablenessOffset { get; set; } = 0;
    /// <summary>Culture's filter on trait expression: neuroticism offset, -100-100.</summary>
    public int NeuroticismOffset { get; set; } = 0;
}
