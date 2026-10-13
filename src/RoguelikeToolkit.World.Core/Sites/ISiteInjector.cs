using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>One site-placement pass over a materialized region/local map.</summary>
public interface ISiteInjector
{
    string Id { get; }
    int Order { get; }
    SiteTier Tier { get; }
    IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context);
}
