namespace RoguelikeToolkit.World.Core.History;

/// <summary>Campaign season of an engagement (hemisphere-relative: winter is the cold season where it is fought).</summary>
public enum HistorySeason : byte
{
    /// <summary>Thaw and mud.</summary>
    Spring = 0,
    /// <summary>The campaigning season.</summary>
    Summer = 1,
    /// <summary>Harvest; stores are full.</summary>
    Autumn = 2,
    /// <summary>Cold; supply and morale suffer.</summary>
    Winter = 3,
}
