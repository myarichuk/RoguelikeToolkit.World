namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Deterministic hashing RNG (SplitMix64). The single shared randomness primitive
/// for all generation stages. Sub-streams derive from (worldSeed, salt) so tile
/// content is independent of iteration order and reproducible on demand.
/// </summary>
public struct Rng
{
    private ulong _state;

    public Rng(ulong state)
    {
        _state = state;
    }

    public static Rng Create(int worldSeed, int salt = 0)
    {
        ulong z = (ulong)(uint)worldSeed + 0x9E3779B97F4A7C15ul + (ulong)(uint)salt * 0xBF58476D1CE4E5B9ul;
        return new Rng(Mix(z));
    }

    /// <summary>
    /// Derives the stored per-tile seed for on-demand generation.
    /// Pure function of (worldSeed, tileIndex).
    /// </summary>
    public static uint DeriveTileSeed(int worldSeed, int tileIndex)
        => Create(worldSeed, tileIndex).NextUInt();

    /// <summary>
    /// Returns an independent sub-stream. Stable for the same (state, salt).
    /// </summary>
    public Rng Derive(int salt)
    {
        ulong z = _state + 0x9E3779B97F4A7C15ul + (ulong)(uint)salt * 0xBF58476D1CE4E5B9ul;
        return new Rng(Mix(z));
    }

    public ulong NextULong()
    {
        ulong z = (_state += 0x9E3779B97F4A7C15ul);
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul);
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EBul);
        return z ^ (z >> 31);
    }

    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// <summary>
    /// Maps a 32-bit draw onto [0, max) by multiply-high (Lemire's method without
    /// the rejection step). Cheap and branch-free, and free of the low-bit skew
    /// of <c>% max</c>, but not exactly uniform: with 2^32 inputs and <c>max</c>
    /// outputs some outputs receive one more input than others, a relative bias
    /// of at most <c>max / 2^32</c> (below 1e-6 for any <c>max</c> under 4096).
    /// Fine for gameplay placement; use <see cref="NextDouble"/> or a rejection
    /// loop where exact uniformity matters.
    /// </summary>
    public uint NextUInt(uint max) => max == 0 ? 0 : (uint)((NextULong() >> 32) * (ulong)max >> 32);

    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    private static ulong Mix(ulong z)
    {
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul);
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EBul);
        return z ^ (z >> 31);
    }
}
