using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The history simulation's managed state: polities and their tile-ownership map, the seeded peoples
/// roster, projected settlements and sites, roads, the append-only event log, and the user-override
/// layer. Lives on <c>World</c> (built by the history stages at orders 26..91), survives
/// <c>World.Recompute</c> (which only re-resolves the spatial projection), and implements
/// <see cref="IHistoricalContext"/> so settlement scoring and tile queries read history through the
/// same hook a game-supplied implementation would serve.
/// </summary>
public sealed class HistoryCatalog : IHistoricalContext
{
    /// <summary>Persistence schema version, written first by <see cref="Save"/> and checked by <see cref="Load"/>: 1 is the P2a shape (capitals only), 2 adds polity demography and road routes, 3 adds P2c treaty/faith state (grudges, windows, faiths, holy sites, villains, heirs), 4 adds the generic-kernel state (string table, declared scalars and per-polity scalar values, polity lifecycle, event ids, causes, importance and participants, grudge turn stamps).</summary>
    public const int SchemaVersion = 4;

    /// <summary>Planet radius in kilometres, matching <c>World.EarthRadiusKm</c> (kept literal: the root slice is above History).</summary>
    private const double EarthRadiusKm = 6371.0;

    private int _appendTurn = int.MinValue;
    private int _appendCount;

    /// <summary>Builds an empty catalog for <paramref name="seed"/>.</summary>
    public HistoryCatalog(int seed)
    {
        Seed = seed;
    }

    /// <summary>World seed; every history draw derives from it (see <see cref="HistoryRng"/>).</summary>
    public int Seed { get; }

    /// <summary>Seeded peoples roster (a copy of the configured pairs); read by <see cref="PolityFormStage"/>.</summary>
    public List<HistoryPeople> Seeds { get; } = new();

    /// <summary>True once <see cref="CultureSeedStage"/> has seeded <see cref="Seeds"/> and synced budgets.</summary>
    public bool CultureSeeded { get; internal set; }

    /// <summary>All polities; a polity's <c>Id</c> is its index here.</summary>
    public List<Polity> Polities { get; } = new();

    /// <summary>True once <see cref="PolityFormStage"/> has founded polities (possibly zero) and projected ownership.</summary>
    public bool PolitiesFormed { get; internal set; }

    /// <summary>Owner polity id per planet tile (-1 unclaimed); sized by <see cref="PolityFormStage"/>.</summary>
    public int[] Ownership { get; internal set; } = Array.Empty<int>();

    /// <summary>Interned identifiers (event kinds, roles); seeded with the built-in kinds.</summary>
    public StringTable Strings { get; } = new();

    private int _nextEventId;

    /// <summary>Append-only event log (foundings, forks, losses); compacts beyond <see cref="MaxEvents"/>.</summary>
    public List<HistoryEvent> Events { get; } = new();

    /// <summary>Declared polity scalars (pressure is built in).</summary>
    public ScalarRegistry Scalars { get; } = new();

    private int _indexVersion = -1, _eventVersion, _indexedCount = -1;
    private readonly Dictionary<int, List<HistoryEvent>> _byTile = new();
    private readonly Dictionary<int, List<HistoryEvent>> _byPolity = new();

    private void EnsureEventIndex()
    {
        if (_indexVersion == _eventVersion && _indexedCount == Events.Count) return;
        _byTile.Clear();
        _byPolity.Clear();
        foreach (var e in Events)
        {
            Index(_byTile, e.TileIndex, e);
            Index(_byPolity, e.PolityId, e);
        }
        _indexVersion = _eventVersion;
        _indexedCount = Events.Count;
    }

    private static void Index(Dictionary<int, List<HistoryEvent>> map, int key, HistoryEvent e)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<HistoryEvent>();
        list.Add(e);
    }

    /// <summary>Per-turn event budget synced from the context (default 64).</summary>
    public int MaxEventsPerTurn { get; internal set; } = 64;

    /// <summary>Event-log cap synced from the context (default 4096).</summary>
    public int MaxEvents { get; internal set; } = 4096;

    /// <summary>Events dropped by the per-turn budget.</summary>
    public int DroppedEvents { get; private set; }

    /// <summary>
    /// Times an operator fell back to built-in folk numbers because the peoples tables failed to
    /// load (see <see cref="PeoplesLoader"/>). Zero on a healthy build; nonzero means the data
    /// layer needs attention, not that the simulation stopped.
    /// </summary>
    public int PeoplesFallbacks { get; internal set; }

    /// <summary>Compaction passes run against the log cap.</summary>
    public int Compactions { get; private set; }

    /// <summary>Projected settlements (P2a: one capital each).</summary>
    public List<HistorySettlement> Settlements { get; } = new();

    /// <summary>Projected historic sites (P2a: one capital site each).</summary>
    public List<HistorySite> Sites { get; } = new();

    /// <summary>Trade-road segments grouped into capital-to-capital routes (see <see cref="HistoryRoad"/>).</summary>
    public List<HistoryRoad> Roads { get; } = new();

    /// <summary>Lasting grievances between polity pairs (canonical order, one entry per pair); repaid only by treaty, tribute or overlay.</summary>
    public List<GrudgeEntry> Grudges { get; } = new();

    /// <summary>Non-aggression windows between polity pairs; <see cref="WarOperator"/> skips windowed pairs.</summary>
    public List<TreatyWindow> Treaties { get; } = new();

    /// <summary>One faith per polity with a raised tradition, in polity-id order; built by <see cref="FaithPostPass"/>.</summary>
    public List<HistoryFaith> Faiths { get; } = new();

    /// <summary>Raised holy sites; project as shrine-kind game sites while active.</summary>
    public List<HistoryHolySite> HolySites { get; } = new();

    /// <summary>Risen long-life villains, bounded by <see cref="HistoryContext.MaxVillains"/>.</summary>
    public List<VillainRecord> Villains { get; } = new();

    /// <summary>User renames and pinned tiles; written by the game, never by a stage.</summary>
    public HistoryOverrides Overrides { get; } = new();

    /// <summary>True after a map edit re-resolved the projection without resimulating history.</summary>
    public bool Stale { get; set; }

    /// <summary>Timeline head at the last ownership-changing edit, or null when never forked.</summary>
    public int? ForkedAtTurn { get; internal set; }

    /// <summary>Last simulated turn (the span head the driver reached).</summary>
    public int SimulatedTurns { get; internal set; }

    /// <summary>Copies the event budgets from the context; called by <see cref="CultureSeedStage"/>.</summary>
    internal void SyncBudgets(int maxEventsPerTurn, int maxEvents)
    {
        MaxEventsPerTurn = maxEventsPerTurn;
        MaxEvents = maxEvents;
    }

    /// <summary>Owner polity id of <paramref name="tileIndex"/>, or -1 when unclaimed or unknown.</summary>
    public int OwnerOf(int tileIndex)
        => (uint)tileIndex < (uint)Ownership.Length ? Ownership[tileIndex] : -1;

    /// <summary>Owning polity of <paramref name="tileIndex"/>, or null when unclaimed or unknown.</summary>
    public Polity? PolityOf(int tileIndex)
    {
        int id = OwnerOf(tileIndex);
        return (uint)id < (uint)Polities.Count ? Polities[id] : null;
    }

    /// <summary>Display name of <paramref name="polityId"/>: the user rename, else a stable fallback label.</summary>
    public string DisplayNameOf(int polityId)
        => Overrides.Renames.TryGetValue(polityId, out var name) ? name : $"polity-{polityId}";

    /// <summary>Every projected settlement of <paramref name="polityId"/>, active and inactive.</summary>
    public IReadOnlyList<HistorySettlement> CitiesOf(int polityId)
    {
        var list = new List<HistorySettlement>();
        foreach (var s in Settlements)
            if (s.PolityId == polityId) list.Add(s);
        return list;
    }

    /// <summary>Every logged event at <paramref name="tileIndex"/>, oldest first (indexed, not a log scan).</summary>
    public IReadOnlyList<HistoryEvent> EventsAtTile(int tileIndex)
    {
        EnsureEventIndex();
        return _byTile.TryGetValue(tileIndex, out var list) ? list : Array.Empty<HistoryEvent>();
    }

    /// <summary>Every logged event of <paramref name="polityId"/>, oldest first.</summary>
    public IReadOnlyList<HistoryEvent> EventsOf(int polityId)
    {
        EnsureEventIndex();
        return _byPolity.TryGetValue(polityId, out var list) ? list : Array.Empty<HistoryEvent>();
    }

    /// <summary>
    /// Adds <paramref name="weight"/> to the grievance between <paramref name="a"/> and
    /// <paramref name="b"/> (canonical pair order; one entry per pair). Grudges never decay with
    /// time: only <see cref="RepayGrudges"/> clears them.
    /// </summary>
    public void AddGrudge(int a, int b, int weight, int turn = -1)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight), "Grudge weight must be positive.");
        if (a == b) throw new ArgumentException("A grudge needs two different polities.");
        (int x, int y) = a < b ? (a, b) : (b, a);
        foreach (var entry in Grudges)
        {
            if (entry.A == x && entry.B == y) { entry.Weight += weight; if (turn >= 0) entry.LastTurn = turn; return; }
        }
        Grudges.Add(new GrudgeEntry { A = x, B = y, Weight = weight, FirstTurn = turn, LastTurn = turn });
    }

    /// <summary>Current grievance weight between <paramref name="a"/> and <paramref name="b"/>, or 0 when repaid or never raised.</summary>
    public int GrudgeWeight(int a, int b)
    {
        (int x, int y) = a < b ? (a, b) : (b, a);
        foreach (var entry in Grudges)
            if (entry.A == x && entry.B == y) return entry.Weight;
        return 0;
    }

    /// <summary>
    /// Repays every grievance between <paramref name="a"/> and <paramref name="b"/> (treaty,
    /// tribute or culture overlay settled it). Returns true when an entry was cleared.
    /// </summary>
    public bool RepayGrudges(int a, int b)
    {
        (int x, int y) = a < b ? (a, b) : (b, a);
        for (int i = 0; i < Grudges.Count; i++)
        {
            if (Grudges[i].A == x && Grudges[i].B == y) { Grudges.RemoveAt(i); return true; }
        }
        return false;
    }

    /// <summary>Adds a non-aggression window between <paramref name="a"/> and <paramref name="b"/> through <paramref name="untilTurn"/> (inclusive).</summary>
    public void AddWindow(int a, int b, int untilTurn)
    {
        if (untilTurn < 0) throw new ArgumentOutOfRangeException(nameof(untilTurn), "Window end cannot be negative.");
        Treaties.Add(new TreatyWindow { A = a, B = b, UntilTurn = untilTurn });
    }

    /// <summary>True when a non-aggression window covers <paramref name="turn"/> between <paramref name="a"/> and <paramref name="b"/> (either direction).</summary>
    public bool IsWindowed(int a, int b, int turn)
    {
        foreach (var window in Treaties)
        {
            if (turn > window.UntilTurn) continue;
            if ((window.A == a && window.B == b) || (window.A == b && window.B == a)) return true;
        }
        return false;
    }

    /// <summary>
    /// Appends an event bypassing the per-turn budget (the log cap still applies).
    /// Reserved for <see cref="Respatialize"/> provenance (fork markers, site losses): a map edit
    /// must never lose its own fork marker to a budget the simulation already spent.
    /// </summary>
    public void AppendPriorityEvent(HistoryEventKind kind, int turn, int polityId, int tileIndex, IReadOnlyList<string> tags)
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Event turn cannot be negative.");
        var entry = NewEvent(kind, turn, polityId, tileIndex, tags);
        Register(entry);
        EnforceCap();
    }

    /// <summary>
    /// Appends an event, enforcing the per-turn budget (excess appends are dropped and counted in
    /// <see cref="DroppedEvents"/>) and the log cap (oldest non-founding events compact first; founding
    /// events are the timeline skeleton and are removed only when nothing else can go). Returns false
    /// when the budget dropped the event.
    /// </summary>
    public bool AppendEvent(HistoryEventKind kind, int turn, int polityId, int tileIndex, IReadOnlyList<string> tags)
        => AppendLinked(kind, turn, polityId, tileIndex, tags, -1, null) >= 0;

    /// <summary>
    /// Budgeted append that also records a <paramref name="cause"/> (an event <see cref="HistoryEvent.Id"/>,
    /// or -1) and participants. Returns the new event's id, or -1 when the per-turn budget dropped it.
    /// </summary>
    internal int AppendLinked(HistoryEventKind kind, int turn, int polityId, int tileIndex,
        IReadOnlyList<string> tags, int cause, IReadOnlyList<EventParticipant>? participants)
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Event turn cannot be negative.");
        if (turn != _appendTurn) { _appendTurn = turn; _appendCount = 0; }
        if (_appendCount >= MaxEventsPerTurn) { DroppedEvents++; return -1; }
        _appendCount++;
        var entry = NewEvent(kind, turn, polityId, tileIndex, tags, cause);
        if (participants != null) entry.Participants.AddRange(participants);
        Register(entry);
        int id = entry.Id;
        EnforceCap();
        return id;
    }

    private static HistoryEvent NewEvent(HistoryEventKind kind, int turn, int polityId, int tileIndex, IReadOnlyList<string> tags, int cause = -1)
    {
        var entry = new HistoryEvent
        {
            Kind = kind, Turn = turn, PolityId = polityId, TileIndex = tileIndex,
            Importance = EventImportanceMap.Of(kind), Cause = cause,
        };
        if (tags != null) entry.Tags.AddRange(tags);
        return entry;
    }

    private void Register(HistoryEvent entry)
    {
        entry.Id = _nextEventId++;
        Events.Add(entry);
        _eventVersion++;
    }

    /// <summary>
    /// Compacts to <see cref="MaxEvents"/>: the oldest <see cref="EventImportance.Minor"/> event goes
    /// first, then the oldest structural one, then the oldest founding one. An event that a retained
    /// event cites as its cause is skipped while anything uncited is left; only when every candidate is
    /// cited does the oldest of the lowest class go, so the cap always holds.
    /// </summary>
    private void EnforceCap()
    {
        if (Events.Count <= MaxEvents) return;
        bool removed = false;
        while (Events.Count > MaxEvents)
        {
            var cited = new HashSet<int>();
            foreach (var e in Events) if (e.Cause >= 0) cited.Add(e.Cause);
            int victim = -1, fallback = -1;
            for (var tier = EventImportance.Minor; tier <= EventImportance.Founding && victim < 0; tier++)
            {
                for (int i = 0; i < Events.Count; i++)
                {
                    if (Events[i].Importance != tier) continue;
                    if (fallback < 0) fallback = i;
                    if (!cited.Contains(Events[i].Id)) { victim = i; break; }
                }
            }
            Events.RemoveAt(victim >= 0 ? victim : Math.Max(fallback, 0));
            _eventVersion++;
            removed = true;
        }
        if (removed) Compactions++;
    }

    /// <summary>
    /// Clears the simulation, keeping <see cref="Seed"/>, the budgets and <see cref="Overrides"/>:
    /// renames and pins survive and are re-applied by the next run. Called by
    /// <c>World.RegenerateHistory</c> before re-running the history band.
    /// </summary>
    public void Reset()
    {
        Seeds.Clear();
        CultureSeeded = false;
        Polities.Clear();
        PolitiesFormed = false;
        Ownership = Array.Empty<int>();
        Events.Clear();
        _eventVersion++;
        _nextEventId = 0;
        Settlements.Clear();
        Sites.Clear();
        Roads.Clear();
        Grudges.Clear();
        Treaties.Clear();
        Faiths.Clear();
        HolySites.Clear();
        Villains.Clear();
        Stale = false;
        ForkedAtTurn = null;
        SimulatedTurns = 0;
        DroppedEvents = 0;
        Compactions = 0;
        PeoplesFallbacks = 0;
        _appendTurn = int.MinValue;
        _appendCount = 0;
    }

    /// <summary>
    /// Re-resolves only the spatial projection against the current physics: ownership is reassigned
    /// from capitals (user pins win), settlement habitability is re-snapshotted, and drowned sites go
    /// inactive with a <see cref="HistoryEventKind.SiteLost"/> entry each. Causes and dates are never
    /// rewritten. Returns true when any tile changed owner, in which case a
    /// <see cref="HistoryEventKind.Fork"/> marker is appended and <see cref="ForkedAtTurn"/> advances
    /// to the timeline head; a null edit changes nothing and never flickers the fork marker.
    /// </summary>
    public bool Respatialize(WorldDataStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        int tileCount = store.TileCount;
        var capitals = new List<int>(Polities.Count);
        foreach (var p in Polities) capitals.Add(p.CapitalTile);
        var pins = new Dictionary<int, int>();
        foreach (var kv in Overrides.PinnedTiles)
        {
            if (kv.Value >= 0 && kv.Value < Polities.Count) pins[kv.Key] = kv.Value;
        }
        int[] next = HistoryTerritory.AssignOwners(store, capitals, pins);
        bool changed = next.Length != Ownership.Length;
        if (!changed)
        {
            for (int i = 0; i < next.Length; i++)
            {
                if (next[i] != Ownership[i]) { changed = true; break; }
            }
        }
        Ownership = next;

        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        foreach (var settlement in Settlements)
        {
            bool land = (uint)settlement.TileIndex < (uint)tileCount
                && (!hasElev || elev[settlement.TileIndex].Height >= HistoryTerritory.LandThreshold);
            if (!land && settlement.Active)
            {
                settlement.Active = false;
                AppendPriorityEvent(HistoryEventKind.SiteLost, SimulatedTurns, settlement.PolityId, settlement.TileIndex,
                    new[] { "settlement" });
            }
            else if (land)
            {
                settlement.Active = true;
                if (hasCapacity)
                {
                    int total = capacity[settlement.TileIndex].Arable + capacity[settlement.TileIndex].Pasture
                        + capacity[settlement.TileIndex].Game + capacity[settlement.TileIndex].Timber;
                    settlement.Habitability = Math.Clamp(total / 1020f, 0f, 1f);
                }
            }
        }
        foreach (var site in Sites)
        {
            site.Active = (uint)site.TileIndex < (uint)tileCount
                && (!hasElev || elev[site.TileIndex].Height >= HistoryTerritory.LandThreshold);
        }
        foreach (var holy in HolySites)
        {
            bool land = (uint)holy.TileIndex < (uint)tileCount
                && (!hasElev || elev[holy.TileIndex].Height >= HistoryTerritory.LandThreshold);
            if (!land && holy.Active)
            {
                holy.Active = false;
                AppendPriorityEvent(HistoryEventKind.SiteLost, SimulatedTurns, holy.PolityId, holy.TileIndex,
                    new[] { "holy" });
            }
            else if (land)
            {
                holy.Active = true;
            }
        }

        if (changed)
        {
            ForkedAtTurn = SimulatedTurns;
            int tiles = 0;
            foreach (int owner in next) if (owner >= 0) tiles += 1;
            AppendPriorityEvent(HistoryEventKind.Fork, SimulatedTurns, -1, -1, new[] { $"tiles:{tiles}" });
        }
        return changed;
    }

    /// <summary>Deterministic bytes of this catalog; two identical simulations produce identical bytes.</summary>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes this catalog to <paramref name="stream"/> with <see cref="SchemaVersion"/> first.
    /// Deterministic: the same catalog always writes the same bytes.
    /// </summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(new[] { 'H', 'I', 'S', 'T' });
        writer.Write(SchemaVersion);
        writer.Write(Seed);
        writer.Write(MaxEventsPerTurn);
        writer.Write(MaxEvents);
        writer.Write(Seeds.Count);
        foreach (var people in Seeds)
        {
            writer.Write(people.FolkKey ?? string.Empty);
            writer.Write(people.CultureKey ?? string.Empty);
        }
        writer.Write(CultureSeeded);
        writer.Write(PolitiesFormed);
        writer.Write(SimulatedTurns);
        writer.Write(Stale);
        writer.Write(ForkedAtTurn.HasValue);
        writer.Write(ForkedAtTurn.GetValueOrDefault());
        writer.Write(Compactions);
        writer.Write(DroppedEvents);
        writer.Write(Strings.Count);
        for (int i = 0; i < Strings.Count; i++) writer.Write(Strings.NameOf(i));
        writer.Write(Scalars.Count);
        for (int i = 0; i < Scalars.Count; i++)
        {
            var def = Scalars[i];
            writer.Write(def.Name);
            writer.Write(def.Min);
            writer.Write(def.Max);
            writer.Write(def.Default);
        }
        writer.Write(_nextEventId);
        writer.Write(Polities.Count);
        foreach (var polity in Polities)
        {
            writer.Write(polity.Id);
            writer.Write((byte)polity.Form);
            writer.Write(polity.CapitalTile);
            writer.Write(polity.FolkKey ?? string.Empty);
            writer.Write(polity.CultureKey ?? string.Empty);
            writer.Write(polity.FoundedTurn);
            writer.Write(polity.Population);
            writer.Write(polity.Pressure);
            writer.Write(polity.CultureOverlay ?? string.Empty);
            writer.Write(polity.RulerGeneration);
            writer.Write(polity.OpennessDrift);
            writer.Write(polity.HeirBornTurn);
            writer.Write(polity.HasVillain);
            writer.Write(polity.EndedTurn);
            writer.Write(polity.EndCause);
            writer.Write(polity.Parent);
            writer.Write(Scalars.Count);
            for (int k = 0; k < Scalars.Count; k++) writer.Write(polity.GetScalar(k, Scalars));
        }
        writer.Write(Ownership.Length);
        foreach (int owner in Ownership) writer.Write(owner);
        writer.Write(Events.Count);
        foreach (var entry in Events)
        {
            writer.Write((byte)entry.Kind);
            writer.Write(entry.Turn);
            writer.Write(entry.PolityId);
            writer.Write(entry.TileIndex);
            writer.Write(entry.Tags.Count);
            foreach (string tag in entry.Tags) writer.Write(tag ?? string.Empty);
            writer.Write(entry.Id);
            writer.Write(entry.Cause);
            writer.Write((byte)entry.Importance);
            writer.Write(entry.Participants.Count);
            foreach (var part in entry.Participants)
            {
                writer.Write(part.EntityId);
                writer.Write(part.Role);
            }
        }
        writer.Write(Settlements.Count);
        foreach (var settlement in Settlements)
        {
            writer.Write(settlement.TileIndex);
            writer.Write(settlement.PolityId);
            writer.Write(settlement.FoundedTurn);
            writer.Write(settlement.Position.Latitude);
            writer.Write(settlement.Position.Longitude);
            writer.Write(settlement.Habitability);
            writer.Write(settlement.Active);
        }
        writer.Write(Sites.Count);
        foreach (var site in Sites)
        {
            writer.Write(site.TileIndex);
            writer.Write(site.PolityId);
            writer.Write(site.FoundedTurn);
            writer.Write(site.Position.Latitude);
            writer.Write(site.Position.Longitude);
            writer.Write(site.Active);
        }
        writer.Write(Roads.Count);
        foreach (var road in Roads)
        {
            writer.Write(road.TileA);
            writer.Write(road.TileB);
            writer.Write(road.PolityId);
            writer.Write(road.RouteA);
            writer.Write(road.RouteB);
            writer.Write(road.FoundedTurn);
            writer.Write(road.Severed);
            writer.Write(road.SeveredTurn);
        }
        writer.Write(Grudges.Count);
        foreach (var grudge in Grudges)
        {
            writer.Write(grudge.A);
            writer.Write(grudge.B);
            writer.Write(grudge.Weight);
            writer.Write(grudge.FirstTurn);
            writer.Write(grudge.LastTurn);
        }
        writer.Write(Treaties.Count);
        foreach (var window in Treaties)
        {
            writer.Write(window.A);
            writer.Write(window.B);
            writer.Write(window.UntilTurn);
        }
        writer.Write(Faiths.Count);
        foreach (var faith in Faiths)
        {
            writer.Write(faith.PolityId);
            writer.Write(faith.Form ?? string.Empty);
            writer.Write(faith.Powers.Count);
            foreach (string power in faith.Powers) writer.Write(power ?? string.Empty);
            writer.Write(faith.MythSlots.Count);
            foreach (string slot in faith.MythSlots) writer.Write(slot ?? string.Empty);
        }
        writer.Write(HolySites.Count);
        foreach (var holy in HolySites)
        {
            writer.Write(holy.TileIndex);
            writer.Write(holy.PolityId);
            writer.Write(holy.FoundedTurn);
            writer.Write(holy.Position.Latitude);
            writer.Write(holy.Position.Longitude);
            writer.Write(holy.Active);
        }
        writer.Write(Villains.Count);
        foreach (var villain in Villains)
        {
            writer.Write(villain.Id);
            writer.Write(villain.PolityId);
            writer.Write(villain.FolkKey ?? string.Empty);
            writer.Write(villain.RisenTurn);
            writer.Write(villain.Agenda ?? string.Empty);
        }
        var renames = new List<KeyValuePair<int, string>>(Overrides.Renames);
        renames.Sort((a, b) => a.Key.CompareTo(b.Key));
        writer.Write(renames.Count);
        foreach (var kv in renames)
        {
            writer.Write(kv.Key);
            writer.Write(kv.Value ?? string.Empty);
        }
        var pins = new List<KeyValuePair<int, int>>(Overrides.PinnedTiles);
        pins.Sort((a, b) => a.Key.CompareTo(b.Key));
        writer.Write(pins.Count);
        foreach (var kv in pins)
        {
            writer.Write(kv.Key);
            writer.Write(kv.Value);
        }
    }

    /// <summary>
    /// Reads a catalog written by <see cref="Save"/>. Throws <see cref="InvalidDataException"/> for a
    /// bad magic, an unknown schema version, or truncated/corrupt content.
    /// </summary>
    public static HistoryCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        try
        {
            char[] magic = reader.ReadChars(4);
            if (magic.Length != 4 || magic[0] != 'H' || magic[1] != 'I' || magic[2] != 'S' || magic[3] != 'T')
                throw new InvalidDataException("Not a history catalog (bad magic).");
            int version = reader.ReadInt32();
            if (version < 1 || version > SchemaVersion)
                throw new InvalidDataException($"Unsupported history schema version {version} (want 1-{SchemaVersion}).");
            var catalog = new HistoryCatalog(reader.ReadInt32())
            {
                MaxEventsPerTurn = reader.ReadInt32(),
                MaxEvents = reader.ReadInt32(),
            };
            int seeds = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < seeds; i++)
                catalog.Seeds.Add(new HistoryPeople { FolkKey = reader.ReadString(), CultureKey = reader.ReadString() });
            catalog.CultureSeeded = reader.ReadBoolean();
            catalog.PolitiesFormed = reader.ReadBoolean();
            catalog.SimulatedTurns = reader.ReadInt32();
            catalog.Stale = reader.ReadBoolean();
            bool hasFork = reader.ReadBoolean();
            int forkedAt = reader.ReadInt32();
            catalog.ForkedAtTurn = hasFork ? forkedAt : null;
            catalog.Compactions = reader.ReadInt32();
            catalog.DroppedEvents = reader.ReadInt32();
            int nextEventId = 0;
            if (version >= 4)
            {
                int names = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < names; i++)
                {
                    string name = reader.ReadString();
                    if (string.IsNullOrWhiteSpace(name) || catalog.Strings.Intern(name) != i)
                        throw new InvalidDataException("Corrupt history catalog (string table).");
                }
                int declared = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < declared; i++)
                {
                    string name = reader.ReadString();
                    double min = reader.ReadDouble(), max = reader.ReadDouble(), def = reader.ReadDouble();
                    try
                    {
                        if (catalog.Scalars.Declare(name, min, max, def).Id != i)
                            throw new InvalidDataException("Corrupt history catalog (scalar table order).");
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidDataException("Corrupt history catalog (scalar definition).", ex);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidDataException("Corrupt history catalog (scalar definition).", ex);
                    }
                }
                nextEventId = reader.ReadInt32();
            }
            int polities = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < polities; i++)
            {
                var polity = new Polity
                {
                    Id = reader.ReadInt32(),
                    Form = (PolityForm)reader.ReadByte(),
                    CapitalTile = reader.ReadInt32(),
                    FolkKey = reader.ReadString(),
                    CultureKey = reader.ReadString(),
                    FoundedTurn = reader.ReadInt32(),
                };
                if (version >= 2)
                {
                    polity.Population = reader.ReadDouble();
                    polity.Pressure = reader.ReadDouble();
                    polity.CultureOverlay = reader.ReadString();
                    polity.RulerGeneration = reader.ReadInt32();
                    polity.OpennessDrift = reader.ReadInt32();
                }
                if (version >= 3)
                {
                    polity.HeirBornTurn = reader.ReadInt32();
                    polity.HasVillain = reader.ReadBoolean();
                }
                if (version >= 4)
                {
                    polity.EndedTurn = reader.ReadInt32();
                    polity.EndCause = reader.ReadInt32();
                    polity.Parent = reader.ReadInt32();
                    int slots = CheckedCount(reader.ReadInt32());
                    if (slots > catalog.Scalars.Count) throw new InvalidDataException("Corrupt history catalog (scalar slots).");
                    for (int k = 0; k < slots; k++) polity.SetScalar(k, reader.ReadDouble(), catalog.Scalars);
                }
                catalog.Polities.Add(polity);
            }
            int tiles = CheckedCount(reader.ReadInt32());
            var ownership = new int[tiles];
            for (int i = 0; i < tiles; i++) ownership[i] = reader.ReadInt32();
            catalog.Ownership = ownership;
            int events = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < events; i++)
            {
                var kind = (HistoryEventKind)reader.ReadByte();
                int eventTurn = reader.ReadInt32(), eventPolity = reader.ReadInt32(), eventTile = reader.ReadInt32();
                int tags = CheckedCount(reader.ReadInt32());
                var tagList = new List<string>(Math.Min(tags, 64));
                for (int k = 0; k < tags; k++) tagList.Add(reader.ReadString());
                int id = -1, cause = -1;
                var importance = EventImportanceMap.Of(kind);
                List<EventParticipant>? partList = null;
                if (version >= 4)
                {
                    id = reader.ReadInt32();
                    cause = reader.ReadInt32();
                    importance = (EventImportance)reader.ReadByte();
                    int parts = CheckedCount(reader.ReadInt32());
                    partList = new List<EventParticipant>(Math.Min(parts, 64));
                    for (int k = 0; k < parts; k++) partList.Add(new EventParticipant(reader.ReadInt32(), reader.ReadInt32()));
                }
                var entry = new HistoryEvent
                {
                    Kind = kind, Turn = eventTurn, PolityId = eventPolity, TileIndex = eventTile,
                    Cause = cause, Importance = importance,
                };
                entry.Tags.AddRange(tagList);
                if (partList != null) entry.Participants.AddRange(partList);
                if (version >= 4)
                {
                    entry.Id = id;
                    catalog.Events.Add(entry);
                    catalog._eventVersion++;
                }
                else catalog.Register(entry);
            }
            int settlements = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < settlements; i++)
            {
                catalog.Settlements.Add(new HistorySettlement
                {
                    TileIndex = reader.ReadInt32(),
                    PolityId = reader.ReadInt32(),
                    FoundedTurn = reader.ReadInt32(),
                    Position = new GeoCoord(reader.ReadDouble(), reader.ReadDouble()),
                    Habitability = reader.ReadSingle(),
                    Active = reader.ReadBoolean(),
                });
            }
            int sites = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < sites; i++)
            {
                catalog.Sites.Add(new HistorySite
                {
                    TileIndex = reader.ReadInt32(),
                    PolityId = reader.ReadInt32(),
                    FoundedTurn = reader.ReadInt32(),
                    Position = new GeoCoord(reader.ReadDouble(), reader.ReadDouble()),
                    Active = reader.ReadBoolean(),
                });
            }
            int roads = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < roads; i++)
            {
                int tileA = reader.ReadInt32();
                int tileB = reader.ReadInt32();
                int keeper = reader.ReadInt32();
                int routeA = 0, routeB = 0, founded = 0, severedTurn = 0;
                bool severed = false;
                if (version >= 2)
                {
                    routeA = reader.ReadInt32();
                    routeB = reader.ReadInt32();
                    founded = reader.ReadInt32();
                    severed = reader.ReadBoolean();
                    severedTurn = reader.ReadInt32();
                }
                var road = new HistoryRoad
                {
                    TileA = tileA,
                    TileB = tileB,
                    PolityId = keeper,
                    RouteA = routeA,
                    RouteB = routeB,
                    FoundedTurn = founded,
                };
                road.Severed = severed;
                road.SeveredTurn = severedTurn;
                catalog.Roads.Add(road);
            }
            if (version >= 3)
            {
                int grudges = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < grudges; i++)
                {
                    var grudge = new GrudgeEntry { A = reader.ReadInt32(), B = reader.ReadInt32(), Weight = reader.ReadInt32() };
                    if (version >= 4) { grudge.FirstTurn = reader.ReadInt32(); grudge.LastTurn = reader.ReadInt32(); }
                    catalog.Grudges.Add(grudge);
                }
                int windows = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < windows; i++)
                    catalog.Treaties.Add(new TreatyWindow { A = reader.ReadInt32(), B = reader.ReadInt32(), UntilTurn = reader.ReadInt32() });
                int faiths = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < faiths; i++)
                {
                    var faith = new HistoryFaith { PolityId = reader.ReadInt32(), Form = reader.ReadString() };
                    int powers = CheckedCount(reader.ReadInt32());
                    for (int k = 0; k < powers; k++) faith.Powers.Add(reader.ReadString());
                    int slots = CheckedCount(reader.ReadInt32());
                    for (int k = 0; k < slots; k++) faith.MythSlots.Add(reader.ReadString());
                    catalog.Faiths.Add(faith);
                }
                int holy = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < holy; i++)
                {
                    catalog.HolySites.Add(new HistoryHolySite
                    {
                        TileIndex = reader.ReadInt32(),
                        PolityId = reader.ReadInt32(),
                        FoundedTurn = reader.ReadInt32(),
                        Position = new GeoCoord(reader.ReadDouble(), reader.ReadDouble()),
                        Active = reader.ReadBoolean(),
                    });
                }
                int villains = CheckedCount(reader.ReadInt32());
                for (int i = 0; i < villains; i++)
                {
                    catalog.Villains.Add(new VillainRecord
                    {
                        Id = reader.ReadInt32(),
                        PolityId = reader.ReadInt32(),
                        FolkKey = reader.ReadString(),
                        RisenTurn = reader.ReadInt32(),
                        Agenda = reader.ReadString(),
                    });
                }
            }
            int renames = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < renames; i++) catalog.Overrides.Renames[reader.ReadInt32()] = reader.ReadString();
            int pins = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < pins; i++) catalog.Overrides.PinnedTiles[reader.ReadInt32()] = reader.ReadInt32();
            if (version >= 4) catalog._nextEventId = nextEventId;
            catalog.ValidateReferences();
            return catalog;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Truncated history catalog.", ex);
        }
    }

    /// <summary>
    /// Rejects a loaded catalog whose polity ids point outside <see cref="Polities"/>, so a corrupt
    /// file throws <see cref="InvalidDataException"/> here instead of failing later in a stage.
    /// Event, ownership and settlement ids may be -1 (none / unclaimed).
    /// </summary>
    private void ValidateReferences()
    {
        int count = Polities.Count;
        void Check(int id, string what, bool allowNone)
        {
            if (id >= count || id < (allowNone ? -1 : 0))
                throw new InvalidDataException($"Corrupt history catalog ({what} references polity {id}, have {count}).");
        }
        for (int i = 0; i < Polities.Count; i++)
            if (Polities[i].Id != i) throw new InvalidDataException($"Corrupt history catalog (polity at index {i} has id {Polities[i].Id}).");
        foreach (int owner in Ownership) Check(owner, "ownership", true);
        foreach (var e in Events) Check(e.PolityId, "event", true);
        foreach (var s in Settlements) Check(s.PolityId, "settlement", true);
        foreach (var s in Sites) Check(s.PolityId, "site", true);
        foreach (var r in Roads) Check(r.PolityId, "road", true);
        foreach (var g in Grudges) { Check(g.A, "grudge", false); Check(g.B, "grudge", false); }
        foreach (var t in Treaties) { Check(t.A, "treaty", false); Check(t.B, "treaty", false); }
        foreach (var f in Faiths) Check(f.PolityId, "faith", false);
        foreach (var h in HolySites) Check(h.PolityId, "holy site", true);
        foreach (var v in Villains) Check(v.PolityId, "villain", false);
        foreach (var p in Polities)
        {
            Check(p.Parent, "polity parent", true);
            if (p.EndCause >= Strings.Count || p.EndCause < -1) throw new InvalidDataException($"Corrupt history catalog (polity {p.Id} end cause {p.EndCause}).");
        }
        var seenIds = new HashSet<int>();
        foreach (var e in Events)
        {
            if (e.Id >= _nextEventId || e.Id < 0 || !seenIds.Add(e.Id))
                throw new InvalidDataException($"Corrupt history catalog (event id {e.Id}).");
            if (e.Cause < -1 || e.Cause >= e.Id) throw new InvalidDataException($"Corrupt history catalog (event {e.Id} cause {e.Cause}).");
            if (e.Importance > EventImportance.Founding) throw new InvalidDataException($"Corrupt history catalog (event {e.Id} importance).");
            foreach (var part in e.Participants)
            {
                Check(part.EntityId, "event participant", false);
                if ((uint)part.Role >= (uint)Strings.Count) throw new InvalidDataException($"Corrupt history catalog (event {e.Id} role {part.Role}).");
            }
        }
    }

    private static int CheckedCount(int count)
        => count >= 0 ? count : throw new InvalidDataException($"Corrupt history catalog (negative count {count}).");

    /// <summary>
    /// War-torn tiles (a war or rebellion battlefield) raise danger by <see cref="WarDangerDelta"/>;
    /// sheltered holy tiles lower it by <see cref="HolyDangerDelta"/>; every other tile has no
    /// history modifier. Both come from tags and state, so stubbed text providers cannot change them.
    /// </summary>
    public bool TryGetTileModifier(int worldTileIndex, out TileHistoryModifier modifier)
    {
        foreach (var entry in EventsAtTile(worldTileIndex))
        {
            if (entry.Kind == HistoryEventKind.War || entry.Kind == HistoryEventKind.Rebellion)
            {
                modifier = new TileHistoryModifier { DangerDelta = WarDangerDelta, HabitabilityDelta = 0f };
                return true;
            }
        }
        foreach (var holy in HolySites)
        {
            if (holy.Active && holy.TileIndex == worldTileIndex)
            {
                modifier = new TileHistoryModifier { DangerDelta = HolyDangerDelta, HabitabilityDelta = 0f };
                return true;
            }
        }
        modifier = default;
        return false;
    }

    /// <summary>Danger added to war-torn tiles (see <see cref="TryGetTileModifier"/>).</summary>
    public const float WarDangerDelta = 2f;

    /// <summary>Danger removed on sheltered holy tiles (see <see cref="TryGetTileModifier"/>).</summary>
    public const float HolyDangerDelta = -1f;

    /// <summary>
    /// Active projected sites within <paramref name="radiusKm"/> of <paramref name="position"/>, nearest
    /// first (ties by tile index). Capitals project as <c>Settlement</c>-kind sites with zero deltas;
    /// war and rebellion battlefields (from the event log) project as <c>Battlefield</c>-kind sites
    /// carrying <see cref="WarDangerDelta"/>; raised holy sites project as <c>Shrine</c>-kind sites
    /// carrying <see cref="HolyDangerDelta"/> (a desecrated holy tile projects as a battlefield).
    /// </summary>
    public IEnumerable<HistoricSite> GetSitesNear(GeoCoord position, double radiusKm)
    {
        var matches = new List<(double Distance, int Tile, HistoricSite Site)>();
        foreach (var site in Sites)
        {
            if (!site.Active) continue;
            double distance = DistanceKm(position, site.Position);
            if (double.IsNaN(distance) || distance > radiusKm) continue;
            bool battlefield = IsBattlefield(site.TileIndex, out int battleTurn);
            var projected = new HistoricSite
            {
                Id = battlefield ? $"hist-battle-{site.TileIndex}-{battleTurn}" : $"hist-site-{site.TileIndex}",
                Kind = battlefield ? HistoricSiteKind.Battlefield : HistoricSiteKind.Settlement,
                TileIndex = site.TileIndex,
                Position = site.Position,
                DangerDelta = battlefield ? WarDangerDelta : 0f,
                HabitabilityDelta = 0f,
            };
            projected.Metadata["polity"] = site.PolityId.ToString();
            projected.Metadata["founded"] = site.FoundedTurn.ToString();
            if (battlefield) projected.Metadata["battle"] = battleTurn.ToString();
            matches.Add((distance, site.TileIndex, projected));
        }
        foreach (var holy in HolySites)
        {
            if (!holy.Active) continue;
            double distance = DistanceKm(position, holy.Position);
            if (double.IsNaN(distance) || distance > radiusKm) continue;
            bool battlefield = IsBattlefield(holy.TileIndex, out int battleTurn);
            var projected = new HistoricSite
            {
                Id = battlefield ? $"hist-battle-{holy.TileIndex}-{battleTurn}" : $"hist-holy-{holy.TileIndex}",
                Kind = battlefield ? HistoricSiteKind.Battlefield : HistoricSiteKind.Shrine,
                TileIndex = holy.TileIndex,
                Position = holy.Position,
                DangerDelta = battlefield ? WarDangerDelta : HolyDangerDelta,
                HabitabilityDelta = 0f,
            };
            projected.Metadata["polity"] = holy.PolityId.ToString();
            projected.Metadata["founded"] = holy.FoundedTurn.ToString();
            projected.Metadata["holy"] = "true";
            if (battlefield) projected.Metadata["battle"] = battleTurn.ToString();
            matches.Add((distance, holy.TileIndex, projected));
        }
        matches.Sort((a, b) =>
        {
            int c = a.Distance.CompareTo(b.Distance);
            return c != 0 ? c : a.Tile.CompareTo(b.Tile);
        });
        foreach (var match in matches) yield return match.Site;
    }

    private bool IsBattlefield(int tileIndex, out int battleTurn)
    {
        battleTurn = 0;
        bool found = false;
        foreach (var entry in EventsAtTile(tileIndex))
        {
            if (entry.Kind != HistoryEventKind.War && entry.Kind != HistoryEventKind.Rebellion) continue;
            if (!found || entry.Turn < battleTurn)
            {
                battleTurn = entry.Turn;
                found = true;
            }
        }
        return found;
    }

    private static double DistanceKm(GeoCoord a, GeoCoord b)
    {
        var va = Vector3D.FromGeoCoord(a);
        var vb = Vector3D.FromGeoCoord(b);
        double dot = Math.Clamp(Vector3D.Dot(va, vb), -1.0, 1.0);
        return DetMath.Acos(dot) * EarthRadiusKm;
    }
}
