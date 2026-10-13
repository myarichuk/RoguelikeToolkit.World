namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>Extensible historic-site kinds. Games can map their own events onto these.</summary>
public enum HistoricSiteKind : byte
{
    /// <summary>A living or founded settlement.</summary>
    Settlement = 0,
    /// <summary>An abandoned or destroyed structure or settlement.</summary>
    Ruin = 1,
    /// <summary>Site of a past battle.</summary>
    Battlefield = 2,
    /// <summary>A sacred or holy place.</summary>
    Shrine = 3,
    /// <summary>Anything that fits none of the other kinds; the default for <see cref="HistoricSite.Kind"/>.</summary>
    Other = 255
}
