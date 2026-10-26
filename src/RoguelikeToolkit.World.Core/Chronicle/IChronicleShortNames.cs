using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Optional companion of <see cref="IChronicleNameResolver"/>: the short form of a realm's name (<c>Varn</c> for <c>the Hold of
/// Varn</c>), used for its second and later mentions within one event, as prose would. Without it every mention is the full
/// name.
/// </summary>
internal interface IChronicleShortNames
{
    /// <summary>The short form of the realm's name, or null when it has none.</summary>
    string? PolityShortName(HistoryCatalog catalog, int polityId);
}
