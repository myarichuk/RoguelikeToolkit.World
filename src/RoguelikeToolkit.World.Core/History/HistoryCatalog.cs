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
    /// <summary>Persistence schema version, written first by <see cref="Save"/> and checked by <see cref="Load"/>: 1 is the P2a shape (capitals only), 2 adds polity demography and road routes.</summary>
    public const int SchemaVersion = 2;

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

    /// <summary>Append-only event log (foundings, forks, losses); compacts beyond <see cref="MaxEvents"/>.</summary>
    public List<HistoryEvent> Events { get; } = new();

    /// <summary>Per-turn event budget synced from the context (default 64).</summary>
    public int MaxEventsPerTurn { get; internal set; } = 64;

    /// <summary>Event-log cap synced from the context (default 4096).</summary>
    public int MaxEvents { get; internal set; } = 4096;

    /// <summary>Events dropped by the per-turn budget.</summary>
    public int DroppedEvents { get; private set; }

    /// <summary>Compaction passes run against the log cap.</summary>
    public int Compactions { get; private set; }

    /// <summary>Projected settlements (P2a: one capital each).</summary>
    public List<HistorySettlement> Settlements { get; } = new();

    /// <summary>Projected historic sites (P2a: one capital site each).</summary>
    public List<HistorySite> Sites { get; } = new();

    /// <summary>Trade-road segments grouped into capital-to-capital routes (see <see cref="HistoryRoad"/>).</summary>
    public List<HistoryRoad> Roads { get; } = new();

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

    /// <summary>Every logged event of <paramref name="polityId"/>, oldest first.</summary>
    public IReadOnlyList<HistoryEvent> EventsOf(int polityId)
    {
        var list = new List<HistoryEvent>();
        foreach (var e in Events)
            if (e.PolityId == polityId) list.Add(e);
        return list;
    }

    /// <summary>
    /// Appends an event, enforcing the per-turn budget (excess appends are dropped and counted in
    /// <see cref="DroppedEvents"/>) and the log cap (oldest non-founding events compact first; founding
    /// events are the timeline skeleton and are removed only when nothing else can go). Returns false
    /// when the budget dropped the event.
    /// </summary>
    public bool AppendEvent(HistoryEventKind kind, int turn, int polityId, int tileIndex, IReadOnlyList<string> tags)
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Event turn cannot be negative.");
        if (turn != _appendTurn) { _appendTurn = turn; _appendCount = 0; }
        if (_appendCount >= MaxEventsPerTurn) { DroppedEvents++; return false; }
        _appendCount++;
        var entry = new HistoryEvent { Kind = kind, Turn = turn, PolityId = polityId, TileIndex = tileIndex };
        if (tags != null) entry.Tags.AddRange(tags);
        Events.Add(entry);
        EnforceCap();
        return true;
    }

    private void EnforceCap()
    {
        if (Events.Count <= MaxEvents) return;
        bool removed = false;
        while (Events.Count > MaxEvents)
        {
            int victim = -1;
            for (int i = 0; i < Events.Count; i++)
            {
                if (Events[i].Kind != HistoryEventKind.Founding) { victim = i; break; }
            }
            Events.RemoveAt(victim >= 0 ? victim : 0);
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
        Settlements.Clear();
        Sites.Clear();
        Roads.Clear();
        Stale = false;
        ForkedAtTurn = null;
        SimulatedTurns = 0;
        DroppedEvents = 0;
        Compactions = 0;
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
                AppendEvent(HistoryEventKind.SiteLost, SimulatedTurns, settlement.PolityId, settlement.TileIndex,
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

        if (changed)
        {
            ForkedAtTurn = SimulatedTurns;
            int tiles = 0;
            foreach (int owner in next) if (owner >= 0) tiles += 1;
            AppendEvent(HistoryEventKind.Fork, SimulatedTurns, -1, -1, new[] { $"tiles:{tiles}" });
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
            if (version != 1 && version != SchemaVersion)
                throw new InvalidDataException($"Unsupported history schema version {version} (want 1 or {SchemaVersion}).");
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
                catalog.Polities.Add(polity);
            }
            int tiles = CheckedCount(reader.ReadInt32());
            var ownership = new int[tiles];
            for (int i = 0; i < tiles; i++) ownership[i] = reader.ReadInt32();
            catalog.Ownership = ownership;
            int events = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < events; i++)
            {
                var entry = new HistoryEvent
                {
                    Kind = (HistoryEventKind)reader.ReadByte(),
                    Turn = reader.ReadInt32(),
                    PolityId = reader.ReadInt32(),
                    TileIndex = reader.ReadInt32(),
                };
                int tags = CheckedCount(reader.ReadInt32());
                for (int k = 0; k < tags; k++) entry.Tags.Add(reader.ReadString());
                catalog.Events.Add(entry);
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
            int renames = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < renames; i++) catalog.Overrides.Renames[reader.ReadInt32()] = reader.ReadString();
            int pins = CheckedCount(reader.ReadInt32());
            for (int i = 0; i < pins; i++) catalog.Overrides.PinnedTiles[reader.ReadInt32()] = reader.ReadInt32();
            return catalog;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Truncated history catalog.", ex);
        }
    }

    private static int CheckedCount(int count)
        => count >= 0 ? count : throw new InvalidDataException($"Corrupt history catalog (negative count {count}).");

    /// <summary>
    /// War-torn tiles (a war or rebellion battlefield) raise danger by <see cref="WarDangerDelta"/>;
    /// every other tile has no history modifier. Battlefield tiles come from the event log, so stubbed
    /// text providers cannot change them.
    /// </summary>
    public bool TryGetTileModifier(int worldTileIndex, out TileHistoryModifier modifier)
    {
        foreach (var entry in Events)
        {
            if (entry.TileIndex != worldTileIndex) continue;
            if (entry.Kind == HistoryEventKind.War || entry.Kind == HistoryEventKind.Rebellion)
            {
                modifier = new TileHistoryModifier { DangerDelta = WarDangerDelta, HabitabilityDelta = 0f };
                return true;
            }
        }
        modifier = default;
        return false;
    }

    /// <summary>Danger added to war-torn tiles (see <see cref="TryGetTileModifier"/>).</summary>
    public const float WarDangerDelta = 2f;

    /// <summary>
    /// Active projected sites within <paramref name="radiusKm"/> of <paramref name="position"/>, nearest
    /// first (ties by tile index). Capitals project as <c>Settlement</c>-kind sites with zero deltas;
    /// war and rebellion battlefields (from the event log) project as <c>Battlefield</c>-kind sites
    /// carrying <see cref="WarDangerDelta"/>.
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
        foreach (var entry in Events)
        {
            if (entry.TileIndex != tileIndex) continue;
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
