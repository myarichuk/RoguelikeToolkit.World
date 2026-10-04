using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>Hex adjacency for <see cref="PopulationGrid.Step{TPolicy,TAdj}"/>. Implement as a struct so the call inlines.</summary>
public interface IAdjacency
{
    /// <summary>Writes the neighbouring cells of <paramref name="cell"/> (at most 8) and returns how many.</summary>
    int GetAdjacent(int cell, Span<int> neighbors);
}

/// <summary>Adjacency over a planet's tile graph.</summary>
public readonly struct WorldAdjacency(WorldDataStore store) : IAdjacency
{
    public int GetAdjacent(int cell, Span<int> neighbors) => store.GetAdjacent(cell, neighbors);
}

/// <summary>
/// The game's rules for aggregate population change. All rates are per tick and are clamped to [0, 1]
/// by the grid. Implement as a struct so the calls inline.
/// </summary>
public interface IPopulationPolicy
{
    double BirthRate(int cell, int group, uint count);
    double DeathRate(int cell, int group, uint count);

    /// <summary>Fraction of the group that leaves the cell this tick.</summary>
    double MigrationRate(int cell, int group, uint count);

    /// <summary>How appealing <paramref name="cell"/> is as a destination for <paramref name="group"/>; zero or less = never.</summary>
    double Attraction(int cell, int group);
}

/// <summary>
/// Aggregate (non-individual) population: a head count per (cell, group), where a group is whatever the
/// game buckets people by (species, faction, profession...). Far from the player nobody is an entity; a
/// cell is just a few counters that <see cref="Step{TPolicy,TAdj}"/> advances in bulk. When the player
/// arrives, the game <see cref="Take"/>s some of them out and spawns real entities (keyed through
/// <see cref="EntityKeyMap"/> so spawning is idempotent), and when they leave it <see cref="Give"/>s the
/// survivors back.
///
/// Counts are stored cell-major (one cell's groups are contiguous), so a tick is one linear sweep over
/// memory, and cells with nobody in them cost a single comparison per group.
///
/// A tick is deterministic for (seed, <see cref="Tick"/>, counts): each cell draws from its own
/// sub-stream, so the result does not depend on iteration order, and it reads the old counts and writes
/// the new ones into a second buffer, so a cell's neighbours cannot see a half-updated tick. Migration
/// conserves people exactly; births and deaths are the expected value stochastically rounded, which keeps
/// the mean right but adds no demographic noise of its own.
/// </summary>
public sealed class PopulationGrid
{
    private const uint Magic = 0x47504F50;   // "POPG"
    private const int Version = 1;
    private const int HeaderSize = 24;

    private readonly int _groups;
    private uint[] _counts;
    private uint[] _next;

    public PopulationGrid(int cellCount, int groupCount)
    {
        if (cellCount < 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
        if (groupCount <= 0) throw new ArgumentOutOfRangeException(nameof(groupCount));
        if ((long)cellCount * groupCount > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(cellCount));
        CellCount = cellCount;
        _groups = groupCount;
        _counts = new uint[cellCount * groupCount];
        _next = new uint[cellCount * groupCount];
    }

    public int CellCount { get; }
    public int GroupCount => _groups;

    /// <summary>Ticks simulated so far; part of the per-cell random stream, so it is saved with the counts.</summary>
    public uint Tick { get; private set; }

    /// <summary>One cell's counts, one entry per group. Writable: edit it to place or remove people directly.</summary>
    public Span<uint> Cell(int cell)
    {
        if ((uint)cell >= (uint)CellCount) throw new ArgumentOutOfRangeException(nameof(cell));
        return _counts.AsSpan(cell * _groups, _groups);
    }

    public uint Get(int cell, int group) => Cell(cell)[CheckGroup(group)];

    /// <summary>Adds people (saturating at <see cref="uint.MaxValue"/>); returns how many actually fit.</summary>
    public uint Give(int cell, int group, uint amount)
    {
        ref uint slot = ref Cell(cell)[CheckGroup(group)];
        uint added = Math.Min(amount, uint.MaxValue - slot);
        slot += added;
        return added;
    }

    /// <summary>Removes up to <paramref name="amount"/> people; returns how many were really there.</summary>
    public uint Take(int cell, int group, uint amount)
    {
        ref uint slot = ref Cell(cell)[CheckGroup(group)];
        uint taken = Math.Min(amount, slot);
        slot -= taken;
        return taken;
    }

    public long Total()
    {
        long sum = 0;
        foreach (uint c in _counts) sum += c;
        return sum;
    }

    public long Total(int group)
    {
        CheckGroup(group);
        long sum = 0;
        for (int i = group; i < _counts.Length; i += _groups) sum += _counts[i];
        return sum;
    }

    /// <summary>
    /// Advances every cell one tick. <paramref name="cells"/> optionally limits the sweep to a subset (for
    /// instance everything outside the player's loaded area, or a round-robin slice); migrants from those
    /// cells may still land anywhere.
    /// </summary>
    public void Step<TPolicy, TAdj>(in TPolicy policy, in TAdj adjacency, int seed, ReadOnlySpan<int> cells = default)
        where TPolicy : struct, IPopulationPolicy
        where TAdj : struct, IAdjacency
    {
        Array.Copy(_counts, _next, _counts.Length);
        var tickRng = Rng.Create(seed, unchecked((int)Tick));

        if (cells.IsEmpty)
            for (int c = 0; c < CellCount; c++) StepCell(c, policy, adjacency, tickRng);
        else
            foreach (int c in cells)
                if ((uint)c < (uint)CellCount) StepCell(c, policy, adjacency, tickRng);

        (_counts, _next) = (_next, _counts);
        Tick++;
    }

    private void StepCell<TPolicy, TAdj>(int cell, in TPolicy policy, in TAdj adjacency, Rng tickRng)
        where TPolicy : struct, IPopulationPolicy
        where TAdj : struct, IAdjacency
    {
        int baseIndex = cell * _groups;
        var here = _counts.AsSpan(baseIndex, _groups);

        bool any = false;
        foreach (uint c in here) if (c != 0) { any = true; break; }
        if (!any) return;

        var rng = tickRng.Derive(cell);
        Span<int> neighbors = stackalloc int[8];
        Span<double> weights = stackalloc double[8];
        int adjacent = -1;   // looked up lazily: a quiet cell never needs it

        for (int g = 0; g < _groups; g++)
        {
            uint count = here[g];
            if (count == 0) continue;

            uint births = RoundStochastic(count * Clamp01(policy.BirthRate(cell, g, count)), ref rng);
            uint deaths = Math.Min(count, RoundStochastic(count * Clamp01(policy.DeathRate(cell, g, count)), ref rng));
            uint survivors = count - deaths;
            uint movers = Math.Min(survivors, RoundStochastic(survivors * Clamp01(policy.MigrationRate(cell, g, count)), ref rng));

            uint leaving = 0;
            if (movers != 0)
            {
                if (adjacent < 0) adjacent = Math.Min(adjacency.GetAdjacent(cell, neighbors), neighbors.Length);
                leaving = Disperse(g, movers, neighbors[..adjacent], weights, policy, ref rng);
            }

            ref uint stay = ref _next[baseIndex + g];
            stay = stay - deaths - leaving;                                    // never underflows: deaths + leaving <= count
            stay += Math.Min(births, uint.MaxValue - stay);
        }
    }

    /// <summary>Splits <paramref name="movers"/> over the neighbours by attraction; returns how many actually left.</summary>
    private uint Disperse<TPolicy>(int group, uint movers, ReadOnlySpan<int> neighbors, Span<double> weights, in TPolicy policy, ref Rng rng)
        where TPolicy : struct, IPopulationPolicy
    {
        double totalWeight = 0;
        int last = -1;
        for (int k = 0; k < neighbors.Length; k++)
        {
            double w = policy.Attraction(neighbors[k], group);
            weights[k] = w > 0 && double.IsFinite(w) ? w : 0;
            totalWeight += weights[k];
            if (weights[k] > 0) last = k;
        }
        if (totalWeight <= 0) return 0;                                         // nowhere to go: they stay

        uint remaining = movers;
        for (int k = 0; k < neighbors.Length && remaining != 0; k++)
        {
            double w = weights[k];
            if (w <= 0) continue;
            uint share = k == last
                ? remaining                                                    // last candidate takes the rest
                : Math.Min(remaining, RoundStochastic(remaining * (w / totalWeight), ref rng));
            totalWeight -= w;
            remaining -= share;

            ref uint dest = ref _next[neighbors[k] * _groups + group];
            uint fit = Math.Min(share, uint.MaxValue - dest);
            dest += fit;
            remaining += share - fit;                                          // a saturated cell bounces them back
        }
        return movers - remaining;
    }

    private static double Clamp01(double rate) => rate > 0 ? (rate < 1 ? rate : 1) : 0;   // NaN falls through to 0

    private static uint RoundStochastic(double expected, ref Rng rng)
    {
        double whole = Math.Floor(expected);
        double frac = expected - whole;
        uint n = whole >= uint.MaxValue ? uint.MaxValue : (uint)whole;
        return frac > 0 && n < uint.MaxValue && rng.NextDouble() < frac ? n + 1 : n;
    }

    private int CheckGroup(int group)
        => (uint)group < (uint)_groups ? group : throw new ArgumentOutOfRangeException(nameof(group));

    // --- persistence --------------------------------------------------------------------------------------

    /// <summary>Writes the counts and tick. A grid is dense (a few MB for a whole planet), so there is no delta form.</summary>
    public void Save(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], CellCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], _groups);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Tick);
        stream.Write(header);
        stream.Write(MemoryMarshal.AsBytes(_counts.AsSpan()));
    }

    /// <summary>Replaces the counts and tick. Throws <see cref="InvalidDataException"/> on a shape mismatch and leaves the grid unchanged.</summary>
    public void Load(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic) throw new InvalidDataException("Not a population grid.");
        if (BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != Version) throw new InvalidDataException("Unsupported population grid version.");
        if (BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != CellCount ||
            BinaryPrimitives.ReadInt32LittleEndian(header[12..]) != _groups)
            throw new InvalidDataException("Population grid shape does not match.");

        var staged = new uint[_counts.Length];
        stream.ReadExactly(MemoryMarshal.AsBytes(staged.AsSpan()));
        _counts = staged;
        Tick = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
    }
}
