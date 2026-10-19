using System.Runtime.InteropServices.JavaScript;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.MapEditor.Engine;

/// <summary>
/// The history half of the worker surface: configuration for the next <see cref="Generate"/>, and batch reads of
/// the current world's <see cref="HistoryCatalog"/> for a time-scrubbing panel. Same interop shape as the rest:
/// one primitive array per call, fixed strides documented on each export (the layout tests pin them), tile-sized
/// data as plain int arrays so JS receives an <c>Int32Array</c>. Entities carry their turn stamps
/// (founded/ended/severed), so the panel filters them by turn itself; ownership, which is not stamped per tile,
/// comes from the recorded timeline (<see cref="HistoryOwnershipAt"/>, or base plus changes).
/// </summary>
public static partial class WorldExports
{
    private static readonly HistoryContext HistoryDefaults = new();
    private static bool _historyEnabled = true;
    private static int _historyTurnCount = HistoryDefaults.TurnCount;
    private static int _historyTurnYears = HistoryDefaults.TurnYears;
    private static int _historyMaxPolities = HistoryDefaults.MaxPolities;
    // Turn length the current world was built with (the config may have changed since).
    private static int _builtTurnYears = HistoryDefaults.TurnYears;

    /// <summary>Stride of <see cref="HistoryPolities"/>.</summary>
    public const int PolityStride = 8;
    /// <summary>Stride of <see cref="HistorySettlements"/>.</summary>
    public const int SettlementStride = 9;
    /// <summary>Stride of <see cref="HistoryRoads"/>.</summary>
    public const int RoadStride = 7;
    /// <summary>Stride of <see cref="HistoryScars"/>.</summary>
    public const int ScarStride = 4;
    /// <summary>Stride of <see cref="HistoryTerritoryChanges"/>.</summary>
    public const int TerritoryChangeStride = 3;
    /// <summary>Length of <see cref="HistoryInfo"/>.</summary>
    public const int HistoryInfoLength = 11;

    /// <summary>
    /// Sets how the next <see cref="Generate"/> runs history: off, or on with <paramref name="turnCount"/> turns of
    /// <paramref name="turnYears"/> years and at most <paramref name="maxPolities"/> founding polities. Applies with or
    /// without a registered plugin; the defaults are Core's (<see cref="HistoryContext"/>). Validated here, so a bad
    /// request throws and keeps the previous configuration.
    /// </summary>
    [JSExport]
    public static void ConfigureHistory(bool enabled, int turnCount, int turnYears, int maxPolities)
    {
        // The context's setters are the validation: same ranges, same messages as a direct Core caller gets.
        _ = new HistoryContext { TurnCount = turnCount, TurnYears = turnYears, MaxPolities = maxPolities };
        _historyEnabled = enabled;
        _historyTurnCount = turnCount;
        _historyTurnYears = turnYears;
        _historyMaxPolities = maxPolities;
    }

    /// <summary>Restores Core's history defaults for the next <see cref="Generate"/>.</summary>
    [JSExport]
    public static void ResetHistoryConfig()
    {
        ConfigureHistory(true, HistoryDefaults.TurnCount, HistoryDefaults.TurnYears, HistoryDefaults.MaxPolities);
        _historyRoster = Array.Empty<(string, string)>();
        _historyFounders = Array.Empty<HistoryFounder>();
    }

    // The peoples roster and pinned founders for the next Generate; empty roster = Core's default, no founders = fully drawn.
    private static (string Folk, string Culture)[] _historyRoster = Array.Empty<(string, string)>();
    private static HistoryFounder[] _historyFounders = Array.Empty<HistoryFounder>();

    /// <summary>The (folk, culture) pairs the wizard offers: one entry per shipped people, in UI order.</summary>
    private static readonly (string Folk, string Culture)[] PeoplePairs =
    {
        ("human", "human_common"), ("dwarf", "dwarven_clan"), ("elf_high", "elven_high"), ("elf_wood", "elven_wood"),
        ("elf_dark", "elven_dark"), ("orc", "orcish_horde"), ("halfling", "halfling_hearth"), ("gnome", "gnomish_burrow"),
        ("lizardfolk", "lizardfolk_marsh"), ("dragonfolk", "dragonfolk_aerie"), ("catfolk", "catfolk_prowl"), ("dogfolk", "dogfolk_pack"),
    };

    /// <summary>
    /// The peoples a world can be founded by, one string each: <c>folk\tculture\tfolkName\tcultureName\tsubsistence\taffinity</c>,
    /// where affinity is <c>Form:weight,Form:weight</c> (empty when the land alone decides). Only pairs whose data loaded and
    /// whose subsistence the folk allows are listed.
    /// </summary>
    [JSExport]
    public static string[] HistoryPeoples()
    {
        var store = Core.Peoples.PeoplesStore.Load();
        var list = new List<string>();
        foreach (var (folk, culture) in PeoplePairs)
        {
            if (!store.Folks.TryGetValue(folk, out var f) || !store.Cultures.TryGetValue(culture, out var c)) continue;
            if (store.PairingProblem(folk, culture) is not null) continue;
            string affinity = string.Join(',', c.FormAffinity.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}"));
            list.Add($"{folk}\t{culture}\t{f.DisplayName}\t{c.DisplayName}\t{c.Subsistence}\t{affinity}");
        }
        return list.ToArray();
    }

    /// <summary>Names of <see cref="PolityForm"/> indexed by value, for the wizard's form picker and the <c>forms</c> argument of <see cref="ConfigureHistoryFounders"/>.</summary>
    [JSExport]
    public static string[] HistoryFormNames() => Enum.GetNames(typeof(PolityForm));

    /// <summary>
    /// Sets the peoples roster the next <see cref="Generate"/> draws founding polities from (parallel arrays of folk and culture
    /// keys; empty restores Core's default roster). Validated here against the shipped data: a bad pair throws and keeps the
    /// previous roster.
    /// </summary>
    [JSExport]
    public static void ConfigureHistoryPeoples(string[] folks, string[] cultures)
    {
        ArgumentNullException.ThrowIfNull(folks);
        ArgumentNullException.ThrowIfNull(cultures);
        if (folks.Length != cultures.Length) throw new ArgumentException("Folk and culture arrays must be the same length.");
        var store = Core.Peoples.PeoplesStore.Load();
        var roster = new (string, string)[folks.Length];
        for (int i = 0; i < folks.Length; i++)
        {
            if (store.PairingProblem(folks[i], cultures[i]) is { } problem) throw new ArgumentException($"Roster entry {i}: {problem}");
            roster[i] = (folks[i], cultures[i]);
        }
        _historyRoster = roster;
    }

    /// <summary>
    /// Pins the first founding polities of the next <see cref="Generate"/> (parallel arrays, one entry per founder; empty clears).
    /// A blank folk or culture is drawn; <paramref name="forms"/> is a <see cref="HistoryFormNames"/> index or -1 for the culture's
    /// affinity and the land; a NaN latitude or longitude leaves the capital to the site scoring.
    /// </summary>
    [JSExport]
    public static void ConfigureHistoryFounders(string[] folks, string[] cultures, int[] forms, double[] latitudes, double[] longitudes)
    {
        int n = folks.Length;
        if (cultures.Length != n || forms.Length != n || latitudes.Length != n || longitudes.Length != n)
            throw new ArgumentException("Founder arrays must all be the same length.");
        var founders = new HistoryFounder[n];
        int formCount = Enum.GetValues(typeof(PolityForm)).Length;
        for (int i = 0; i < n; i++)
        {
            if (forms[i] < -1 || forms[i] >= formCount) throw new ArgumentOutOfRangeException(nameof(forms), $"Founder {i}: form {forms[i]} is not a polity form.");
            bool placed = !double.IsNaN(latitudes[i]) && !double.IsNaN(longitudes[i]);
            founders[i] = new HistoryFounder
            {
                FolkKey = folks[i] ?? string.Empty, CultureKey = cultures[i] ?? string.Empty,
                Form = forms[i] < 0 ? null : (PolityForm)forms[i],
                Latitude = placed ? latitudes[i] : null, Longitude = placed ? longitudes[i] : null,
            };
        }
        _historyFounders = founders;
    }

    /// <summary>Applies the configured history to <paramref name="builder"/>; false when history is off.</summary>
    private static bool ApplyHistoryConfig(WorldBuilder builder)
    {
        _builtTurnYears = _historyTurnYears;
        if (!_historyEnabled)
        {
            builder.WithoutHistory();
            return false;
        }
        builder.WithHistory(c =>
        {
            c.TurnCount = _historyTurnCount; c.TurnYears = _historyTurnYears; c.MaxPolities = _historyMaxPolities;
            if (_historyRoster.Length > 0)
            {
                c.Peoples.Clear();
                foreach (var (folk, culture) in _historyRoster) c.Peoples.Add(new HistoryPeople { FolkKey = folk, CultureKey = culture });
            }
            foreach (var founder in _historyFounders) c.Founders.Add(founder);
        });
        return true;
    }

    /// <summary>
    /// <c>[recorded, simulatedTurns, turnYears, polities, settlements, roads, scars, events, territoryChanges, scalars, samples]</c>
    /// (<see cref="HistoryInfoLength"/> ints): the sizes a panel needs before paging. <c>recorded</c> is 1 when a timeline exists.
    /// </summary>
    [JSExport]
    public static int[] HistoryInfo()
    {
        var h = Current.History;
        return new[]
        {
            h.Timeline.Recorded ? 1 : 0, h.SimulatedTurns, _builtTurnYears, h.Polities.Count, h.Settlements.Count, h.Roads.Count,
            h.Scars.Count, h.Events.Count, h.Timeline.Changes.Count, h.Scalars.Count, h.Timeline.Samples.Count,
        };
    }

    /// <summary>Owner polity per tile at the end of <paramref name="turn"/> (-1 unclaimed); one int per tile, or empty when history is off.</summary>
    [JSExport]
    public static int[] HistoryOwnershipAt(int turn) => Current.History.OwnershipAt(turn);

    /// <summary>Owner polity per tile at turn 0, one int per tile; replay <see cref="HistoryTerritoryChanges"/> over it to scrub without a call per turn.</summary>
    [JSExport]
    public static int[] HistoryOwnershipBase() => (int[])Current.History.Timeline.BaseOwnership.Clone();

    /// <summary><c>(turn, tile, owner)</c> triples, by turn then tile: from that turn on the tile belongs to <c>owner</c>.</summary>
    [JSExport]
    public static int[] HistoryTerritoryChanges()
    {
        var changes = Current.History.Timeline.Changes;
        var result = new int[changes.Count * TerritoryChangeStride];
        for (int i = 0; i < changes.Count; i++)
        {
            result[i * 3] = changes[i].Turn;
            result[i * 3 + 1] = changes[i].Tile;
            result[i * 3 + 2] = changes[i].Owner;
        }
        return result;
    }

    /// <summary>
    /// Per polity (<see cref="PolityStride"/> ints): id, form (<see cref="PolityForm"/>), capital tile, founded turn,
    /// ended turn (-1 alive), parent (-1), end cause (a <see cref="HistoryStrings"/> index, -1), has villain (0/1).
    /// </summary>
    [JSExport]
    public static int[] HistoryPolities()
    {
        var polities = Current.History.Polities;
        var result = new int[polities.Count * PolityStride];
        for (int i = 0; i < polities.Count; i++)
        {
            var p = polities[i];
            int o = i * PolityStride;
            result[o] = p.Id; result[o + 1] = (int)p.Form; result[o + 2] = p.CapitalTile; result[o + 3] = p.FoundedTurn;
            result[o + 4] = p.EndedTurn; result[o + 5] = p.Parent; result[o + 6] = p.EndCause; result[o + 7] = p.HasVillain ? 1 : 0;
        }
        return result;
    }

    /// <summary>Per polity, aligned with <see cref="HistoryPolities"/>: <c>folk\tculture\tcultureOverlay\tdisplayName</c>.</summary>
    [JSExport]
    public static string[] HistoryPolityKeys()
    {
        var h = Current.History;
        return h.Polities.Select(p => $"{p.FolkKey}\t{p.CultureKey}\t{p.CultureOverlay}\t{h.DisplayNameOf(p.Id)}").ToArray();
    }

    /// <summary>
    /// Per settlement (<see cref="SettlementStride"/> ints): tile, polity (end-state owner), founded turn, ended turn (-1 standing),
    /// tier (<see cref="SettlementTier"/>), role (<see cref="SettlementRole"/>), parent settlement (-1), active (0/1), population (rounded).
    /// </summary>
    [JSExport]
    public static int[] HistorySettlements()
    {
        var settlements = Current.History.Settlements;
        var result = new int[settlements.Count * SettlementStride];
        for (int i = 0; i < settlements.Count; i++)
        {
            var s = settlements[i];
            int o = i * SettlementStride;
            result[o] = s.TileIndex; result[o + 1] = s.PolityId; result[o + 2] = s.FoundedTurn; result[o + 3] = s.EndedTurn;
            result[o + 4] = (int)s.Tier; result[o + 5] = (int)s.Role; result[o + 6] = s.Parent; result[o + 7] = s.Active ? 1 : 0;
            result[o + 8] = (int)Math.Round(Math.Clamp(s.Population, 0, int.MaxValue));
        }
        return result;
    }

    /// <summary>Per settlement, aligned with <see cref="HistorySettlements"/>: its end cause, empty while standing.</summary>
    [JSExport]
    public static string[] HistorySettlementEndCauses() => Current.History.Settlements.Select(s => s.EndCause).ToArray();

    /// <summary>Per road segment (<see cref="RoadStride"/> ints): tile A, tile B, keeper polity, route A, route B, founded turn, severed turn (-1 intact).</summary>
    [JSExport]
    public static int[] HistoryRoads()
    {
        var roads = Current.History.Roads;
        var result = new int[roads.Count * RoadStride];
        for (int i = 0; i < roads.Count; i++)
        {
            var r = roads[i];
            int o = i * RoadStride;
            result[o] = r.TileA; result[o + 1] = r.TileB; result[o + 2] = r.PolityId; result[o + 3] = r.RouteA;
            result[o + 4] = r.RouteB; result[o + 5] = r.FoundedTurn; result[o + 6] = r.Severed ? r.SeveredTurn : -1;
        }
        return result;
    }

    /// <summary>Per scar (<see cref="ScarStride"/> ints): tile, polity, founded turn, active (0/1). Kind and cause are in <see cref="HistoryScarKinds"/>.</summary>
    [JSExport]
    public static int[] HistoryScars()
    {
        var scars = Current.History.Scars;
        var result = new int[scars.Count * ScarStride];
        for (int i = 0; i < scars.Count; i++)
        {
            var s = scars[i];
            int o = i * ScarStride;
            result[o] = s.TileIndex; result[o + 1] = s.PolityId; result[o + 2] = s.FoundedTurn; result[o + 3] = s.Active ? 1 : 0;
        }
        return result;
    }

    /// <summary>Per scar, aligned with <see cref="HistoryScars"/>: <c>kind\tcause</c>.</summary>
    [JSExport]
    public static string[] HistoryScarKinds() => Current.History.Scars.Select(s => $"{s.Kind}\t{s.Cause}").ToArray();

    /// <summary>The catalog's interned strings (participant roles, polity end causes), indexed by id.</summary>
    [JSExport]
    public static string[] HistoryStrings()
    {
        var strings = Current.History.Strings;
        var result = new string[strings.Count];
        for (int i = 0; i < result.Length; i++) result[i] = strings.NameOf(i);
        return result;
    }

    /// <summary>Names of <see cref="HistoryEventKind"/> indexed by value (the <c>kind</c> field of <see cref="HistoryEvents"/>).</summary>
    [JSExport]
    public static string[] HistoryEventKindNames()
    {
        var values = (HistoryEventKind[])Enum.GetValues(typeof(HistoryEventKind));
        var result = new string[values.Max(v => (int)v) + 1];
        Array.Fill(result, string.Empty);
        foreach (var v in values) result[(int)v] = v.ToString();
        return result;
    }

    /// <summary>
    /// One page of the event log in log order (ids stay stable; positions shift only when the log compacts, i.e. on the next
    /// <see cref="Generate"/>): <c>[n, (id, kind, turn, polity, tile, cause, importance, parts, (entity, role)...)...]</c>.
    /// <c>kind</c> indexes <see cref="HistoryEventKindNames"/>, <c>role</c> indexes <see cref="HistoryStrings"/>, <c>cause</c>
    /// is an event id or -1. A page past the end is <c>[0]</c>.
    /// </summary>
    [JSExport]
    public static int[] HistoryEvents(int start, int count)
    {
        var page = EventPage(start, count);
        var list = new List<int>(1 + page.Count * 8) { page.Count };
        foreach (var e in page)
        {
            list.Add(e.Id); list.Add((int)e.Kind); list.Add(e.Turn); list.Add(e.PolityId); list.Add(e.TileIndex);
            list.Add(e.Cause); list.Add((int)e.Importance); list.Add(e.Participants.Count);
            foreach (var part in e.Participants) { list.Add(part.EntityId); list.Add(part.Role); }
        }
        return list.ToArray();
    }

    /// <summary>The same page as <see cref="HistoryEvents"/>, one string per event: <c>pluginKind</c> (empty for built-ins) then each tag, tab-joined.</summary>
    [JSExport]
    public static string[] HistoryEventText(int start, int count)
        => EventPage(start, count).Select(e => e.Tags.Count == 0 ? e.PluginKind : e.PluginKind + "\t" + string.Join('\t', e.Tags)).ToArray();

    private static List<HistoryEvent> EventPage(int start, int count)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Page start cannot be negative.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Page size cannot be negative.");
        var events = Current.History.Events;
        int from = Math.Min(start, events.Count);
        return events.GetRange(from, Math.Min(count, events.Count - from));
    }

    /// <summary>
    /// Every polity sample: <c>[width, (turn, polity, population, scalars[width])...]</c>, stride <c>3 + width</c>, by turn then
    /// polity. <c>width</c> is the declared scalar count (names in <see cref="HistorySummaryJson"/>, pressure first); a scalar
    /// declared after a sample was taken reads as its default there.
    /// </summary>
    [JSExport]
    public static double[] HistoryPolitySeries()
    {
        var h = Current.History;
        int width = h.Scalars.Count, stride = 3 + width;
        var samples = h.Timeline.Samples;
        var result = new double[1 + samples.Count * stride];
        result[0] = width;
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            int o = 1 + i * stride;
            result[o] = s.Turn; result[o + 1] = s.PolityId; result[o + 2] = s.Population;
            for (int k = 0; k < width; k++) result[o + 3 + k] = k < s.Scalars.Length ? s.Scalars[k] : h.Scalars[k].Default;
        }
        return result;
    }
}
