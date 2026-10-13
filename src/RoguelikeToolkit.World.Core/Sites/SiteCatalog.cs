using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Managed sparse store for injector-placed sites (overlay, not a dense layer).</summary>
public sealed class SiteCatalog
{
    public List<PlacedSite> Sites { get; } = new();

    public void Add(PlacedSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        Sites.Add(site);
    }

    public IReadOnlyList<PlacedSite> AtCell(int cellIndex)
    {
        var found = new List<PlacedSite>();
        foreach (var s in Sites)
        {
            if (s.CellIndex == cellIndex) found.Add(s);
        }
        return found;
    }

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
