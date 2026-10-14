namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// One drift step applied to an ethnos: a subsistence, language, value or faith
/// shift with its turn, target, cause and before/after values (for example
/// pastoral to agrarian on settling). Game-supplied history input: the library
/// applies deltas through <see cref="Ethnos.Resolve"/> and stores nothing itself.
/// </summary>
public readonly record struct CultureDelta
{
    /// <summary>Canonical <see cref="Trait"/> for a subsistence-archetype shift (for example pastoral to agrarian on settling).</summary>
    public const string Subsistence = "subsistence";
    /// <summary>Canonical <see cref="Trait"/> for a language shift: <see cref="To"/> is the new naming table key (alias <c>namingTable</c>).</summary>
    public const string Language = "language";
    /// <summary>Canonical <see cref="Trait"/> for a faith-form shift (alias <c>faithForm</c>).</summary>
    public const string Faith = "faith";
    /// <summary>Canonical <see cref="Trait"/> for a lineage-default shift, one of patrilineal, matrilineal or bilineal (alias <c>lineageDefault</c>).</summary>
    public const string Lineage = "lineage";
    /// <summary>Canonical <see cref="Trait"/> for a xenophobia shift: <see cref="To"/> is an integer 0-100.</summary>
    public const string Xenophobia = "xenophobia";
    /// <summary>Canonical <see cref="Trait"/> for a hierarchy-claim shift: <see cref="To"/> is an integer 0-100 (alias <c>hierarchyClaim</c>).</summary>
    public const string Hierarchy = "hierarchy";
    /// <summary>Canonical <see cref="Trait"/> for a collectivism shift: <see cref="To"/> is an integer 0-100.</summary>
    public const string Collectivism = "collectivism";
    /// <summary>Canonical <see cref="Trait"/> for an honor shift: <see cref="To"/> is an integer 0-100.</summary>
    public const string Honor = "honor";
    /// <summary>Canonical <see cref="Trait"/> for a mixed-ancestry naming-strategy shift, one of blend, dominant or hybrid (alias <c>mixedNaming</c>).</summary>
    public const string Mixed = "mixed";

    /// <summary>
    /// Builds one drift step.
    /// </summary>
    /// <param name="turn">History turn the shift lands on; turns start at 0 and apply in order.</param>
    /// <param name="target">Who drifts: a culture key, a folk key, or <c>*</c> for the whole ethnos.</param>
    /// <param name="trait">What shifts: one of the canonical trait names above (case-insensitive, aliases in remarks).</param>
    /// <param name="from">Value before the shift, for provenance; blank means unknown and is never checked.</param>
    /// <param name="to">Value after the shift; must be non-blank.</param>
    /// <param name="cause">Why it shifted (settle, conquest, trade, ...); empty means unstated.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="turn"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="target"/>, <paramref name="trait"/> or <paramref name="to"/> is blank.</exception>
    public CultureDelta(int turn, string target, string trait, string from, string to, string cause = "")
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Turn cannot be negative.");
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Target must be non-blank.", nameof(target));
        if (string.IsNullOrWhiteSpace(trait)) throw new ArgumentException("Trait must be non-blank.", nameof(trait));
        if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("To must be non-blank.", nameof(to));
        Turn = turn;
        Target = target;
        Trait = trait;
        From = from ?? string.Empty;
        To = to;
        Cause = cause ?? string.Empty;
    }

    /// <summary>History turn the shift lands on; turns start at 0 and apply in order.</summary>
    public int Turn { get; init; }
    /// <summary>Who drifts: a culture key, a folk key, or <c>*</c> for the whole ethnos.</summary>
    public string Target { get; init; }
    /// <summary>What shifts: a canonical trait name (case-insensitive).</summary>
    public string Trait { get; init; }
    /// <summary>Value before the shift, for provenance; blank means unknown and is never checked.</summary>
    public string From { get; init; }
    /// <summary>Value after the shift.</summary>
    public string To { get; init; }
    /// <summary>Why it shifted (settle, conquest, trade, ...); empty means unstated.</summary>
    public string Cause { get; init; }
}
