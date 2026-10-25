namespace RoguelikeToolkit.World.Core.History;

/// <summary>What a <see cref="HistoryPerson"/> is in the story: the coarse role operators dispatch on. Offices (chancellor,
/// marshal...) are data, held through <see cref="HistoryTenure"/>.</summary>
public enum PersonKind : byte
{
    /// <summary>Rules a polity.</summary>
    Ruler = 0,
    /// <summary>Stands to inherit a polity.</summary>
    Heir = 1,
    /// <summary>Sits on a ruler's council.</summary>
    Advisor = 2,
    /// <summary>Leads armies.</summary>
    General = 3,
    /// <summary>Runs a polity's spies.</summary>
    Spymaster = 4,
    /// <summary>A trading magnate of a town or port.</summary>
    MerchantPrince = 5,
    /// <summary>Leads an uprising.</summary>
    RebelLeader = 6,
    /// <summary>Leads a band outside the law.</summary>
    Outlaw = 7,
    /// <summary>A raider at sea with a patron's licence.</summary>
    Privateer = 8,
    /// <summary>A visionary who stirs the faithful.</summary>
    Prophet = 9,
}
