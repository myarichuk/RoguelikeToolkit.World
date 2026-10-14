using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The history RNG scheme: every draw derives from <c>(seed, stage, turn, entity, purpose)</c>
/// through independent sub-streams, so operator order and iteration order never change outcomes.
/// Stages never share a stream and never accumulate floating point across unordered iteration.
/// </summary>
public static class HistoryRng
{
    /// <summary>
    /// The stream for one purpose: a pure function of its inputs, so the same tuple always yields
    /// the same sequence and different purposes give independent streams.
    /// </summary>
    /// <param name="seed">World seed.</param>
    /// <param name="stage">Stage name (e.g. <c>PolityForm</c>).</param>
    /// <param name="turn">History turn the draw belongs to.</param>
    /// <param name="entityId">Polity or actor id the draw belongs to (0 for world-level draws).</param>
    /// <param name="purpose">What the draw is for (e.g. <c>capital</c>); different purposes never share state.</param>
    public static Rng Create(int seed, string stage, int turn, int entityId, string purpose)
    {
        var stream = Rng.Create(seed, StableHash(stage));
        stream = stream.Derive(turn);
        stream = stream.Derive(entityId);
        stream = stream.Derive(StableHash(purpose ?? string.Empty));
        return stream;
    }

    /// <summary>
    /// Stable 32-bit FNV-1a hash of <paramref name="text"/> for stream salts.
    /// Unlike <c>string.GetHashCode</c> it is identical on every runtime and process.
    /// </summary>
    public static int StableHash(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text ?? string.Empty)
        {
            h ^= c;
            h *= 16777619u;
        }
        return (int)h;
    }
}
