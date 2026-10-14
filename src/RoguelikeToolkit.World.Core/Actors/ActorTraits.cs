using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Actors;

/// <summary>
/// One actor's Big Five (OCEAN) personality, five bytes rolled once from
/// <c>Rng.Create(seed, actorId)</c> against the folk's priors. Traits are decision
/// weights for history operators, never gates; the owning culture filters expression.
/// </summary>
public readonly record struct ActorTraits(byte Openness, byte Conscientiousness, byte Extraversion, byte Agreeableness, byte Neuroticism)
{
    /// <summary>
    /// Rolls one trait set. The stream is a pure function of <paramref name="seed"/> and
    /// <paramref name="id"/> (drawn in O, C, E, A, N order), so the same pair always yields
    /// the same traits and operator order can never change outcomes.
    /// </summary>
    /// <param name="seed">World seed.</param>
    /// <param name="id">Actor id; must not be <see cref="ActorId.None"/>.</param>
    /// <param name="priors">Five weights in O, C, E, A, N order (see <see cref="TraitWeight"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is none.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="priors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="priors"/> does not hold exactly five weights.</exception>
    public static ActorTraits Roll(int seed, ActorId id, TraitWeight[] priors)
    {
        if (id.IsNone) throw new ArgumentException("Cannot roll traits for ActorId.None.", nameof(id));
        if (priors is null) throw new ArgumentNullException(nameof(priors));
        if (priors.Length != 5) throw new ArgumentException("Priors must hold exactly five weights in O, C, E, A, N order.", nameof(priors));
        // A fixed actor salt keeps actor streams off the tile-seed streams: Rng.Create
        // takes (seed, salt) and tile code passes raw tile indexes as salts.
        var rng = Rng.Create(seed, unchecked(id.Value * 7919 + 101));
        return new ActorTraits(RollOne(ref rng, priors[0]), RollOne(ref rng, priors[1]),
            RollOne(ref rng, priors[2]), RollOne(ref rng, priors[3]), RollOne(ref rng, priors[4]));
    }

    private static byte RollOne(ref Rng rng, TraitWeight prior) => prior switch
    {
        TraitWeight.Low => (byte)rng.NextUInt(128),
        TraitWeight.High => (byte)(128 + rng.NextUInt(128)),
        _ => (byte)rng.NextUInt(256),
    };
}
