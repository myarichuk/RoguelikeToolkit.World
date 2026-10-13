using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Query-time options. Null History means pure geography.</summary>
public sealed class QueryOptions
{
    public IHistoricalContext? History { get; init; }

    /// <summary>Materialized-map overrides/bindings. Null means pure geography (current behavior).</summary>
    public IMaterializationStore? Materialized { get; init; }

    public static QueryOptions Default { get; } = new QueryOptions();
}
