namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>Extensible historic-site kinds. Games can map their own events onto these.</summary>
public enum HistoricSiteKind : byte
{
    Settlement = 0,
    Ruin = 1,
    Battlefield = 2,
    Shrine = 3,
    Other = 255
}
