namespace RoguelikeToolkit.World.Core.Queries;

/// <summary>Unified feature kinds for spatial queries.</summary>
public enum FeatureKind : byte
{
    /// <summary>A river reach (including lone river tiles too short to form a catalog reach).</summary>
    River = 0,
    /// <summary>Open water: ocean or lake tiles, plus tiles of cataloged water bodies.</summary>
    WaterBody = 1,
    /// <summary>A mountain range tile from the range catalog.</summary>
    Range = 2,
    /// <summary>A glaciated tile.</summary>
    Glacier = 3,
    /// <summary>A tile holding at least one mineral deposit.</summary>
    Deposit = 4,
    /// <summary>A tile holding an injector-placed site (<see cref="Sites.PlacedSite"/>).</summary>
    Site = 5,
}
