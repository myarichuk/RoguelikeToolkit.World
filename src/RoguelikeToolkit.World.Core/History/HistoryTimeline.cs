namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// What the history looked like at every turn, not only at the end: the ownership map before the first
/// simulated turn, a sparse log of every tile that changed hands since, and one sample per living polity per
/// turn. Recorded by <see cref="HistoryDriver"/>; <see cref="HistoryCatalog.Respatialize"/> logs its re-flood at the
/// timeline head, so <see cref="OwnershipAt"/> at <see cref="HistoryCatalog.SimulatedTurns"/> always equals
/// <see cref="HistoryCatalog.Ownership"/>.
/// </summary>
public sealed class HistoryTimeline
{
    private readonly List<TerritoryChange> _changes = new();
    private readonly List<PolitySample> _samples = new();

    /// <summary>Owner per tile at turn 0 (after polities formed, before turn 1), or empty when no driver ran.</summary>
    public int[] BaseOwnership { get; internal set; } = Array.Empty<int>();

    /// <summary>Every tile reassignment since <see cref="BaseOwnership"/>, by turn then tile.</summary>
    public IReadOnlyList<TerritoryChange> Changes => _changes;

    /// <summary>Per-turn polity samples, by turn then polity id.</summary>
    public IReadOnlyList<PolitySample> Samples => _samples;

    /// <summary>True when a driver recorded this timeline (a base ownership map exists).</summary>
    public bool Recorded => BaseOwnership.Length > 0;

    /// <summary>
    /// Owner per tile at the end of <paramref name="turn"/>: <see cref="BaseOwnership"/> with every change up to and
    /// including that turn applied. An unrecorded timeline returns a copy of <paramref name="current"/>.
    /// </summary>
    public int[] OwnershipAt(int turn, int[] current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!Recorded) return (int[])current.Clone();
        var owners = (int[])BaseOwnership.Clone();
        foreach (var c in _changes)
        {
            if (c.Turn > turn) break;
            owners[c.Tile] = c.Owner;
        }
        return owners;
    }

    /// <summary>The samples of <paramref name="polityId"/>, oldest first.</summary>
    public IReadOnlyList<PolitySample> SamplesOf(int polityId)
    {
        var list = new List<PolitySample>();
        foreach (var s in _samples)
            if (s.PolityId == polityId) list.Add(s);
        return list;
    }

    internal void Clear()
    {
        BaseOwnership = Array.Empty<int>();
        _changes.Clear();
        _samples.Clear();
    }

    /// <summary>Starts a fresh timeline from the formed state: the current ownership is the base, and turn 0 is sampled.</summary>
    internal void Begin(HistoryCatalog history)
    {
        Clear();
        BaseOwnership = (int[])history.Ownership.Clone();
        Sample(history, 0);
    }

    /// <summary>Logs every tile whose owner differs from <paramref name="before"/>, stamped <paramref name="turn"/>.</summary>
    internal void RecordTerritory(int turn, int[] before, int[] after)
    {
        if (!Recorded || before.Length != after.Length) return;
        for (int t = 0; t < after.Length; t++)
            if (before[t] != after[t]) _changes.Add(new TerritoryChange(turn, t, after[t]));
    }

    /// <summary>Samples every living polity at the end of <paramref name="turn"/>.</summary>
    internal void Sample(HistoryCatalog history, int turn)
    {
        if (!Recorded) return;
        var reg = history.Scalars;
        foreach (var p in history.Polities)
        {
            if (!p.Alive) continue;
            var values = new double[reg.Count];
            for (int k = 0; k < values.Length; k++) values[k] = p.GetScalar(k, reg);
            _samples.Add(new PolitySample { Turn = turn, PolityId = p.Id, Population = p.Population, Scalars = values });
        }
    }

    internal void AddLoaded(TerritoryChange change) => _changes.Add(change);

    internal void AddLoaded(PolitySample sample) => _samples.Add(sample);

    /// <summary>Rejects a loaded timeline that does not fit its catalog (see <see cref="HistoryCatalog.Load"/>).</summary>
    internal void Validate(HistoryCatalog history)
    {
        if (!Recorded)
        {
            if (_changes.Count > 0 || _samples.Count > 0) throw new InvalidDataException("Corrupt history catalog (timeline without a base).");
            return;
        }
        int polities = history.Polities.Count;
        if (BaseOwnership.Length != history.Ownership.Length) throw new InvalidDataException("Corrupt history catalog (timeline base size).");
        foreach (int owner in BaseOwnership)
            if (owner < -1 || owner >= polities) throw new InvalidDataException($"Corrupt history catalog (timeline base references polity {owner}).");
        int lastTurn = 0, lastTile = -1;
        foreach (var c in _changes)
        {
            if (c.Turn < lastTurn || c.Turn > history.SimulatedTurns || (uint)c.Tile >= (uint)BaseOwnership.Length || c.Owner < -1 || c.Owner >= polities)
                throw new InvalidDataException($"Corrupt history catalog (territory change at turn {c.Turn}, tile {c.Tile}).");
            // A respatialize re-flood appends a second batch at the head turn; only that turn may restart its tile order.
            if (c.Turn == lastTurn && c.Tile <= lastTile && c.Turn != history.SimulatedTurns)
                throw new InvalidDataException($"Corrupt history catalog (territory change order at turn {c.Turn}).");
            lastTurn = c.Turn;
            lastTile = c.Tile;
        }
        int width = history.Scalars.Count, sampleTurn = 0, lastPolity = -1;
        foreach (var s in _samples)
        {
            if (s.Turn < sampleTurn || s.Turn > history.SimulatedTurns || (uint)s.PolityId >= (uint)polities
                || (s.Turn == sampleTurn && s.PolityId <= lastPolity) || s.Scalars.Length > width || !double.IsFinite(s.Population))
                throw new InvalidDataException($"Corrupt history catalog (polity sample at turn {s.Turn}, polity {s.PolityId}).");
            foreach (double v in s.Scalars)
                if (!double.IsFinite(v)) throw new InvalidDataException($"Corrupt history catalog (polity sample at turn {s.Turn}, polity {s.PolityId}).");
            sampleTurn = s.Turn;
            lastPolity = s.PolityId;
        }
    }
}
