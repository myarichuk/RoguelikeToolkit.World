namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Opt-in parts of the history expansion (docs/HISTORY_EXPANSION_PLAN.md). Every flag off is the legacy simulation, byte for
/// byte in what it decides: the kernel types exist and persist, but no operator writes them.
/// </summary>
[Flags]
public enum HistoryFeatures
{
    /// <summary>The legacy simulation.</summary>
    None = 0,
    /// <summary>Named persons: rulers, heirs, office holders, their relations and tenures.</summary>
    Persons = 1 << 0,
    /// <summary>Field battles resolved in phases instead of a coin flip.</summary>
    Battles = 1 << 1,
    /// <summary>Sieges and relief battles.</summary>
    Sieges = 1 << 2,
    /// <summary>Supply lines, route safety, forts and waystations.</summary>
    Logistics = 1 << 3,
    /// <summary>Caravans, traffic and merchant princes.</summary>
    Commerce = 1 << 4,
    /// <summary>Outlaw bands, havens and privateers.</summary>
    Outlaws = 1 << 5,
    /// <summary>Court influence, plots, sabotage and sponsored banditry.</summary>
    Intrigue = 1 << 6,
    /// <summary>Everything.</summary>
    All = Persons | Battles | Sieges | Logistics | Commerce | Outlaws | Intrigue,
}
