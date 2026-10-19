using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Binds a culture's <see cref="CultureDef.FormAffinity"/> (form names as data) to <see cref="PolityForm"/>: Peoples sits
/// below History and cannot name the enum. A culture with no affinity leaves the form to the land.
/// </summary>
public static class FormAffinity
{
    /// <summary>How much more likely a form is when the land favours it (the geography pick's weight multiplier).</summary>
    public const int LandBonus = 3;

    /// <summary>The culture's weight per <see cref="PolityForm"/> (indexed by value), or <c>null</c> when it has no usable affinity.</summary>
    public static int[]? WeightsOf(CultureDef? culture)
    {
        if (culture is null || culture.FormAffinity is null || culture.FormAffinity.Count == 0) return null;
        var weights = new int[Enum.GetValues(typeof(PolityForm)).Length];
        bool any = false;
        foreach (var (name, weight) in culture.FormAffinity)
        {
            if (!Enum.TryParse(name.Trim(), ignoreCase: true, out PolityForm form) || !Enum.IsDefined(typeof(PolityForm), form)) continue;
            weights[(int)form] = Math.Max(0, weight);
            any |= weight > 0;
        }
        return any ? weights : null;
    }

    /// <summary>Picks a form: each weight, times <see cref="LandBonus"/> for the land's own pick, drawn with <paramref name="unit"/> in [0, 1).</summary>
    public static PolityForm Pick(int[] weights, PolityForm landForm, double unit)
    {
        long total = 0;
        var scaled = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++) { scaled[i] = (long)weights[i] * (i == (int)landForm ? LandBonus : 1); total += scaled[i]; }
        if (total <= 0) return landForm;
        double at = unit * total;
        for (int i = 0; i < scaled.Length; i++)
        {
            if (at < scaled[i]) return (PolityForm)i;
            at -= scaled[i];
        }
        return landForm;
    }

    /// <summary>Affinity names that match no <see cref="PolityForm"/> in the loaded cultures, as one message each (a typo would silently never apply).</summary>
    public static IReadOnlyList<string> Problems(PeoplesStore store)
    {
        var problems = new List<string>();
        foreach (var c in store.Cultures.Values.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
            foreach (var name in c.FormAffinity.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                if (!Enum.TryParse(name.Trim(), ignoreCase: true, out PolityForm form) || !Enum.IsDefined(typeof(PolityForm), form))
                    problems.Add($"Cultures/{c.Key}.yaml: FormAffinity names unknown form '{name}' (want {string.Join(", ", Enum.GetNames(typeof(PolityForm)))}).");
        return problems;
    }
}
