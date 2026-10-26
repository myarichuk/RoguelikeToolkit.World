using System.Globalization;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Names;

namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// The chronicle's own names when the host supplies none: drawn from the shipped name tables in Latin script, keyed on the
/// world seed and the entity (a person on its <see cref="HistoryPerson.StableKey"/>), so the same history always reads the
/// same. A realm reads as its people's realm title for its form (<c>the Hold of Varn</c>); <see cref="HistoryOverrides.Renames"/>
/// win. Realms are named all at once in id order and never share a name. A settlement is named in the tongue of the realm that
/// founded it, so a captured town keeps its name. One instance caches one catalog; build a new one per render pass.
/// </summary>
internal sealed class DefaultChronicleNames : IChronicleNameResolver, IChronicleShortNames
{
    private const int Attempts = 6;

    private HistoryCatalog? _catalog;
    private string[] _realms = Array.Empty<string>();
    private string?[] _short = Array.Empty<string?>();
    private readonly Dictionary<int, string> _persons = new();
    private readonly Dictionary<int, string> _settlements = new();
    private Dictionary<int, int>? _founders;

    /// <inheritdoc />
    public string PolityName(HistoryCatalog catalog, int polityId)
    {
        Bind(catalog);
        if (catalog.Overrides.Renames.TryGetValue(polityId, out var renamed)) return renamed;
        if ((uint)polityId >= (uint)catalog.Polities.Count) return catalog.DisplayNameOf(polityId);
        if (_realms.Length != catalog.Polities.Count) NameRealms(catalog);
        return _realms[polityId];
    }

    /// <inheritdoc />
    public string? PolityShortName(HistoryCatalog catalog, int polityId)
    {
        Bind(catalog);
        if (catalog.Overrides.Renames.ContainsKey(polityId) || (uint)polityId >= (uint)catalog.Polities.Count) return null;
        if (_realms.Length != catalog.Polities.Count) NameRealms(catalog);
        return _short[polityId];
    }

    /// <inheritdoc />
    public string PersonName(HistoryCatalog catalog, int personId)
    {
        Bind(catalog);
        if ((uint)personId >= (uint)catalog.Persons.Count) return "a nameless one";
        if (_persons.TryGetValue(personId, out var cached)) return cached;
        var p = catalog.Persons[personId];
        int seed = Seed(catalog.Seed, "person", p.StableKey);
        string name;
        try
        {
            name = FantasyNameGenerator.GeneratePerson(Race(p.FolkKey), Cultures.FantasyCommon, seed, feminine: p.Feminine, script: NameScript.Latin).Text;
        }
        catch (Exception ex) when (ex is UnknownNameKeyException or NameDataException or ArgumentException)
        {
            name = string.Empty;
        }
        if (string.IsNullOrWhiteSpace(name)) name = "one called " + personId.ToString(CultureInfo.InvariantCulture);
        _persons[personId] = name;
        return name;
    }

    /// <inheritdoc />
    public string SettlementName(HistoryCatalog catalog, int settlementIndex)
    {
        Bind(catalog);
        if ((uint)settlementIndex >= (uint)catalog.Settlements.Count) return "a forgotten place";
        if (_settlements.TryGetValue(settlementIndex, out var cached)) return cached;
        var s = catalog.Settlements[settlementIndex];
        _founders ??= Founders(catalog);
        int founder = _founders.TryGetValue(settlementIndex, out var f) ? f : s.PolityId;
        string folk = (uint)founder < (uint)catalog.Polities.Count ? catalog.Polities[founder].FolkKey : "human";
        string kind = s.Role switch
        {
            SettlementRole.Post => PlaceKinds.Hold,
            SettlementRole.Waystation => PlaceKinds.Camp,
            _ => s.Tier switch
            {
                SettlementTier.City => PlaceKinds.City,
                SettlementTier.Town => PlaceKinds.Town,
                _ => PlaceKinds.Village,
            },
        };
        string name = Place(kind, folk, Seed(catalog.Seed, "settlement", s.TileIndex), 0);
        if (string.IsNullOrWhiteSpace(name)) name = "the place by tile " + s.TileIndex.ToString(CultureInfo.InvariantCulture);
        _settlements[settlementIndex] = name;
        return name;
    }

    private void Bind(HistoryCatalog catalog)
    {
        if (ReferenceEquals(_catalog, catalog)) return;
        _catalog = catalog;
        _realms = Array.Empty<string>();
        _short = Array.Empty<string?>();
        _persons.Clear();
        _settlements.Clear();
        _founders = null;
    }

    private void NameRealms(HistoryCatalog catalog)
    {
        var names = new string[catalog.Polities.Count];
        var shorts = new string?[catalog.Polities.Count];
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in catalog.Overrides.Renames.Values) taken.Add(v);
        for (int id = 0; id < names.Length; id++)
        {
            var p = catalog.Polities[id];
            string race = Race(p.FolkKey);
            IReadOnlyList<string> kinds, titles;
            try
            {
                (kinds, titles) = FantasyNameGenerator.RealmNaming(race, Cultures.FantasyCommon, p.Form.ToString());
            }
            catch (Exception ex) when (ex is UnknownNameKeyException or NameDataException or ArgumentException)
            {
                (kinds, titles) = (Array.Empty<string>(), Array.Empty<string>());
            }
            string name = string.Empty;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                int seed = Seed(catalog.Seed, "realm", id);
                uint pick = (uint)seed + (uint)attempt * 2654435761u;
                string kind = kinds.Count > 0 ? kinds[(int)(pick % (uint)kinds.Count)] : PlaceKinds.City;
                string title = titles.Count > 0 ? titles[(int)((pick >> 8) % (uint)titles.Count)] : "{n}";
                string n = Place(kind, p.FolkKey, seed, attempt);
                if (n.Length == 0) continue;
                string full = title.Replace("{n}", n);
                if (full.StartsWith("The ", StringComparison.Ordinal)) full = "the " + full.Substring(4);
                else if (!title.StartsWith("{n}", StringComparison.Ordinal) && !full.StartsWith("the ", StringComparison.Ordinal)) full = "the " + full;
                name = full;
                shorts[id] = full == n ? null : n;
                if (taken.Add(name)) break;
            }
            names[id] = name.Length > 0 ? name : catalog.DisplayNameOf(id);
        }
        _realms = names;
        _short = shorts;
    }

    private static string Place(string kind, string folk, int seed, int salt)
    {
        try
        {
            return FantasyNameGenerator.GeneratePlace(kind, new PlaceContext { Kind = kind }, Race(folk), Cultures.FantasyCommon, seed,
                salt: salt, script: NameScript.Latin).Text;
        }
        catch (Exception ex) when (ex is UnknownNameKeyException or NameDataException or ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>The settlement → founding polity map: the polity of the event that founded it (a capital's founding, or its own).</summary>
    private static Dictionary<int, int> Founders(HistoryCatalog catalog)
    {
        var byTile = new Dictionary<int, int>();
        for (int i = 0; i < catalog.Settlements.Count; i++)
            if (!byTile.ContainsKey(catalog.Settlements[i].TileIndex)) byTile[catalog.Settlements[i].TileIndex] = i;
        var map = new Dictionary<int, int>();
        foreach (var e in catalog.Events)
        {
            if (e.PolityId < 0) continue;
            if (e.Kind == HistoryEventKind.SettlementFounded)
            {
                int s = ChronicleFactBuilder.TagInt(e, "settlement:");
                if (s >= 0 && !map.ContainsKey(s)) map[s] = e.PolityId;
            }
            else if (e.Kind == HistoryEventKind.Founding && byTile.TryGetValue(e.TileIndex, out int cap) && !map.ContainsKey(cap))
                map[cap] = e.PolityId;
        }
        return map;
    }

    private static string Race(string folk) => string.IsNullOrWhiteSpace(folk) ? FantasyRaces.Human : folk;

    private static int Seed(int worldSeed, string purpose, int entity)
        => (int)HistoryRng.Create(worldSeed, "ChronicleNames", 0, entity, purpose).NextUInt();
}
