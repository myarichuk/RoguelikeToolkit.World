namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>Managed sparse store for injector-placed sites (overlay, not a dense layer).</summary>
public sealed class SiteCatalog
{
    /// <summary>All sites in insertion order. This is the live list; adding to it directly bypasses the null check in <see cref="Add"/>.</summary>
    public List<PlacedSite> Sites { get; } = new();

    /// <summary>Appends a site.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="site"/> is null.</exception>
    public void Add(PlacedSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        Sites.Add(site);
    }

    /// <summary>Sites on one cell in insertion order, as a new list (linear scan); empty when none.</summary>
    public IReadOnlyList<PlacedSite> AtCell(int cellIndex)
    {
        var found = new List<PlacedSite>();
        foreach (var s in Sites)
        {
            if (s.CellIndex == cellIndex) found.Add(s);
        }
        return found;
    }

    /// <summary>Sites of one kind in insertion order, as a new list (linear scan); empty when none.</summary>
    public IReadOnlyList<PlacedSite> OfKind(SiteKind kind)
    {
        var found = new List<PlacedSite>();
        foreach (var s in Sites)
        {
            if (s.Kind == kind) found.Add(s);
        }
        return found;
    }

    /// <summary>Summed danger/habitability deltas on one cell.</summary>
    public (float DangerDelta, float HabitabilityDelta) GetCellModifier(int cellIndex)
    {
        float danger = 0f, hab = 0f;
        foreach (var s in Sites)
        {
            if (s.CellIndex != cellIndex) continue;
            danger += s.DangerDelta;
            hab += s.HabitabilityDelta;
        }
        return (danger, hab);
    }
}
