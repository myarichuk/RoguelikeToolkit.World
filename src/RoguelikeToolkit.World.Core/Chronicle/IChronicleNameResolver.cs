using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// The host's names for the chronicle: how a realm, a person and a settlement read in prose. A game or editor that already
/// names these (its own realm titles, saved person names) supplies an implementation through
/// <see cref="ChronicleOptions.Names"/>; otherwise the chronicle draws names from the shipped name tables, deterministically
/// from the world seed, and honours <see cref="HistoryOverrides.Renames"/>. Implementations must be pure (the same arguments
/// give the same name) and must not change the catalog.
/// </summary>
public interface IChronicleNameResolver
{
    /// <summary>The name of a realm as it reads mid-sentence, with its article where English wants one (<c>the Hold of Varn</c>).</summary>
    /// <param name="catalog">The history.</param>
    /// <param name="polityId">Index into <see cref="HistoryCatalog.Polities"/>.</param>
    string PolityName(HistoryCatalog catalog, int polityId);

    /// <summary>The name of a person (<c>Brannoc Ashgrave</c>), without epithet or office.</summary>
    /// <param name="catalog">The history.</param>
    /// <param name="personId">Index into <see cref="HistoryCatalog.Persons"/>.</param>
    string PersonName(HistoryCatalog catalog, int personId);

    /// <summary>The name of a settlement (<c>Oakmere</c>).</summary>
    /// <param name="catalog">The history.</param>
    /// <param name="settlementIndex">Index into <see cref="HistoryCatalog.Settlements"/>.</param>
    string SettlementName(HistoryCatalog catalog, int settlementIndex);
}
