using System.Globalization;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// One drifted people: a folk plus its current culture plus accumulated deltas,
/// resolved at query time like <see cref="TileHistoryModifier"/>. A dwarf clan
/// raised by humans drifts through deltas alone: no code change is needed to
/// move it from clan names to blended place-nouns, the resolution does it.
/// </summary>
public sealed class Ethnos
{
    /// <summary>
    /// Builds one ethnos.
    /// </summary>
    /// <param name="folk">Folk key (see <see cref="PeoplesStore.FolkKeys"/>); must be non-blank.</param>
    /// <param name="cultureKey">Current culture key (see <see cref="PeoplesStore.Cultures"/>); must be non-blank. Deltas move it from here.</param>
    /// <param name="deltas">Drift steps in any order; <see cref="Resolve"/> applies them by turn.</param>
    /// <exception cref="ArgumentException"><paramref name="folk"/> or <paramref name="cultureKey"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="deltas"/> is null (omit it for none).</exception>
    public Ethnos(string folk, string cultureKey, IEnumerable<CultureDelta>? deltas = null)
    {
        if (string.IsNullOrWhiteSpace(folk)) throw new ArgumentException("Folk must be non-blank.", nameof(folk));
        if (string.IsNullOrWhiteSpace(cultureKey)) throw new ArgumentException("Culture key must be non-blank.", nameof(cultureKey));
        Folk = folk;
        CultureKey = cultureKey;
        Deltas = deltas is null ? Array.Empty<CultureDelta>() : deltas.ToArray();
    }

    /// <summary>Folk key this ethnos belongs to.</summary>
    public string Folk { get; }
    /// <summary>Culture key the ethnos drifts from; deltas move the resolved culture away from it.</summary>
    public string CultureKey { get; }
    /// <summary>Drift steps in declaration order.</summary>
    public IReadOnlyList<CultureDelta> Deltas { get; }

    /// <summary>
    /// Resolves the effective culture at a history turn: the base culture with
    /// every delta at or before <paramref name="turn"/> applied in turn order
    /// (ties break by trait, then value, so resolution is deterministic). A
    /// delta applies when its target is <c>*</c>, this ethnos's folk, or its
    /// culture key. The same inputs always resolve to the same culture.
    /// </summary>
    /// <param name="store">Folk/culture tables the base culture is read from.</param>
    /// <param name="turn">History turn to resolve at; deltas land on their own turn, so turn 0 is pre-drift.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="turn"/> is negative.</exception>
    /// <exception cref="UnknownPeoplesKeyException">The culture key is unknown.</exception>
    /// <exception cref="ArgumentException">A delta names an unknown trait or a value outside its range.</exception>
    public CultureDef Resolve(PeoplesStore store, int turn)
    {
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Turn cannot be negative.");
        var baseCulture = store.GetCulture(CultureKey);
        var resolved = new CultureDef
        {
            Key = baseCulture.Key,
            DisplayName = baseCulture.DisplayName,
            NamingTable = baseCulture.NamingTable,
            Subsistence = baseCulture.Subsistence,
            LineageDefault = baseCulture.LineageDefault,
            Xenophobia = baseCulture.Xenophobia,
            HierarchyClaim = baseCulture.HierarchyClaim,
            Collectivism = baseCulture.Collectivism,
            Honor = baseCulture.Honor,
            FaithForm = baseCulture.FaithForm,
            MixedNaming = baseCulture.MixedNaming,
            DominantShareThreshold = baseCulture.DominantShareThreshold,
            OpennessOffset = baseCulture.OpennessOffset,
            ConscientiousnessOffset = baseCulture.ConscientiousnessOffset,
            ExtraversionOffset = baseCulture.ExtraversionOffset,
            AgreeablenessOffset = baseCulture.AgreeablenessOffset,
            NeuroticismOffset = baseCulture.NeuroticismOffset,
        };
        foreach (var delta in Deltas
                     .Where(d => d.Turn <= turn && MatchesTarget(d))
                     .OrderBy(d => d.Turn)
                     .ThenBy(d => d.Trait, StringComparer.Ordinal)
                     .ThenBy(d => d.To, StringComparer.Ordinal))
            Apply(resolved, delta);
        return resolved;
    }

    private bool MatchesTarget(CultureDelta delta)
        => delta.Target == "*"
           || delta.Target.Equals(Folk, StringComparison.OrdinalIgnoreCase)
           || delta.Target.Equals(CultureKey, StringComparison.OrdinalIgnoreCase);

    private static void Apply(CultureDef culture, CultureDelta delta)
    {
        switch (delta.Trait.Trim().ToLowerInvariant())
        {
            case CultureDelta.Subsistence:
                culture.Subsistence = delta.To;
                break;
            case CultureDelta.Language:
            case "namingtable":
                culture.NamingTable = delta.To;
                break;
            case CultureDelta.Faith:
            case "faithform":
                culture.FaithForm = delta.To;
                break;
            case CultureDelta.Lineage:
            case "lineagedefault":
                culture.LineageDefault = CheckedLineage(delta);
                break;
            case CultureDelta.Xenophobia:
                culture.Xenophobia = CheckedValue(delta, 0, 100);
                break;
            case CultureDelta.Hierarchy:
            case "hierarchyclaim":
                culture.HierarchyClaim = CheckedValue(delta, 0, 100);
                break;
            case CultureDelta.Collectivism:
                culture.Collectivism = CheckedValue(delta, 0, 100);
                break;
            case CultureDelta.Honor:
                culture.Honor = CheckedValue(delta, 0, 100);
                break;
            case CultureDelta.Mixed:
            case "mixednaming":
                culture.MixedNaming = CheckedMixed(delta);
                break;
            case "openness":
            case "opennessoffset":
                culture.OpennessOffset = CheckedValue(delta, -100, 100);
                break;
            case "conscientiousness":
            case "conscientiousnessoffset":
                culture.ConscientiousnessOffset = CheckedValue(delta, -100, 100);
                break;
            case "extraversion":
            case "extraversionoffset":
                culture.ExtraversionOffset = CheckedValue(delta, -100, 100);
                break;
            case "agreeableness":
            case "agreeablenessoffset":
                culture.AgreeablenessOffset = CheckedValue(delta, -100, 100);
                break;
            case "neuroticism":
            case "neuroticismoffset":
                culture.NeuroticismOffset = CheckedValue(delta, -100, 100);
                break;
            case "threshold":
            case "dominantshare":
            case "dominantsharethreshold":
                culture.DominantShareThreshold = CheckedThreshold(delta);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown culture trait '{delta.Trait}' for '{delta.Target}' (want subsistence, language, faith, lineage, a value axis, mixed, an expression offset or threshold).",
                    nameof(delta));
        }
    }

    private static int CheckedValue(CultureDelta delta, int lo, int hi)
    {
        if (!int.TryParse(delta.To, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < lo || v > hi)
            throw new ArgumentException($"Trait '{delta.Trait}' value '{delta.To}' is outside {lo}-{hi}.", nameof(delta));
        return v;
    }

    private static double CheckedThreshold(CultureDelta delta)
    {
        if (!double.TryParse(delta.To, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0.5 || v > 1)
            throw new ArgumentException($"Trait '{delta.Trait}' value '{delta.To}' is outside 0.5-1.", nameof(delta));
        return v;
    }

    private static string CheckedLineage(CultureDelta delta)
    {
        if (delta.To.Equals("patrilineal", StringComparison.OrdinalIgnoreCase)
            || delta.To.Equals("matrilineal", StringComparison.OrdinalIgnoreCase)
            || delta.To.Equals("bilineal", StringComparison.OrdinalIgnoreCase))
            return delta.To;
        throw new ArgumentException(
            $"Trait '{delta.Trait}' value '{delta.To}' is unknown (want Patrilineal, Matrilineal or Bilineal).", nameof(delta));
    }

    private static string CheckedMixed(CultureDelta delta)
    {
        if (delta.To.Equals("blend", StringComparison.OrdinalIgnoreCase)
            || delta.To.Equals("dominant", StringComparison.OrdinalIgnoreCase)
            || delta.To.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
            return delta.To;
        throw new ArgumentException(
            $"Trait '{delta.Trait}' value '{delta.To}' is unknown (want Blend, Dominant or Hybrid).", nameof(delta));
    }
}
