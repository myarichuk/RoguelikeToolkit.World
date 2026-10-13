namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>Query-time options. Null History means pure geography.</summary>
public sealed class QueryOptions
{
    /// <summary>Game history consulted at query time for danger and habitability adjustments; null means pure geography.</summary>
    public IHistoricalContext? History { get; init; }

    /// <summary>Materialized-map overrides/bindings. Null means pure geography (current behavior).</summary>
    public IMaterializationStore? Materialized { get; init; }

    /// <summary>Shared instance with no history and no materialized state (pure geography).</summary>
    public static QueryOptions Default { get; } = new QueryOptions();
}
