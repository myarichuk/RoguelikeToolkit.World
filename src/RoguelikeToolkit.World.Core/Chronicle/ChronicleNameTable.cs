using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Host names as tables indexed by id (realm, person, settlement), for a host that has already named things and wants the
/// chronicle to agree with what it shows. A missing or blank entry falls back to the chronicle's own name for it (the shipped
/// name tables, <see cref="HistoryOverrides.Renames"/> first). A realm's short name (<c>Varn</c> for <c>the Kingdom of Varn</c>)
/// is used for its later mentions within an event, as prose would; without one every mention is the full name.
/// </summary>
public sealed class ChronicleNameTable : IChronicleNameResolver, IChronicleShortNames
{
    private readonly IReadOnlyList<string> _realms, _realmShort, _persons, _settlements;
    private readonly DefaultChronicleNames _fallback = new();

    /// <summary>Builds the table; any list may be empty.</summary>
    /// <param name="realms">Realm names as they read mid-sentence, article included (<c>the Kingdom of Varn</c>), by polity id.</param>
    /// <param name="realmShort">Short realm names (<c>Varn</c>), by polity id.</param>
    /// <param name="persons">Person names, by person id.</param>
    /// <param name="settlements">Settlement names, by settlement index.</param>
    public ChronicleNameTable(IReadOnlyList<string>? realms = null, IReadOnlyList<string>? realmShort = null, IReadOnlyList<string>? persons = null,
        IReadOnlyList<string>? settlements = null)
    {
        _realms = realms ?? Array.Empty<string>();
        _realmShort = realmShort ?? Array.Empty<string>();
        _persons = persons ?? Array.Empty<string>();
        _settlements = settlements ?? Array.Empty<string>();
    }

    private static string? At(IReadOnlyList<string> table, int id) => (uint)id < (uint)table.Count && !string.IsNullOrWhiteSpace(table[id]) ? table[id] : null;

    /// <inheritdoc />
    public string PolityName(HistoryCatalog catalog, int polityId)
        => catalog.Overrides.Renames.TryGetValue(polityId, out var renamed) ? renamed : At(_realms, polityId) ?? _fallback.PolityName(catalog, polityId);

    /// <inheritdoc />
    public string? PolityShortName(HistoryCatalog catalog, int polityId)
    {
        if (catalog.Overrides.Renames.ContainsKey(polityId)) return null;
        // A host's full name without its short form reads in full every time; the fallback's short form belongs to the fallback name.
        return At(_realms, polityId) != null ? At(_realmShort, polityId) : _fallback.PolityShortName(catalog, polityId);
    }

    /// <inheritdoc />
    public string PersonName(HistoryCatalog catalog, int personId) => At(_persons, personId) ?? _fallback.PersonName(catalog, personId);

    /// <inheritdoc />
    public string SettlementName(HistoryCatalog catalog, int settlementIndex)
        => At(_settlements, settlementIndex) ?? _fallback.SettlementName(catalog, settlementIndex);
}
