using RoguelikeToolkit.World.Core.Lineage;

namespace RoguelikeToolkit.World.Core.Actors;

/// <summary>
/// One simulated person: lineage, rolled personality, house, adulthood and faith.
/// Geography-free and name-free by construction: an actor is identified by
/// <see cref="ActorId"/> plus folk/house tags, and names are rendered late from those.
/// </summary>
public sealed class Actor
{
    /// <summary>Stable handle; never <see cref="ActorId.None"/> for a created actor.</summary>
    public ActorId Id { get; }
    /// <summary>Primary folk key (the implied status of <see cref="Ancestry"/>, or <c>mixed</c>).</summary>
    public string Folk { get; }
    /// <summary>Lineage shares; the top entries define the implied status.</summary>
    public Ancestry Ancestry { get; }
    /// <summary>Rolled OCEAN traits (see <see cref="ActorTraits"/>).</summary>
    public ActorTraits Traits { get; }
    /// <summary>House/clan id within the owning polity; 0 means houseless.</summary>
    public int HouseId { get; }
    /// <summary>True when the actor's age reached the folk's maturation age (see <see cref="IsAdultAge"/>).</summary>
    public bool IsAdult { get; }
    /// <summary>Faith id the actor follows; empty means none.</summary>
    public string FaithId { get; }

    private Actor(ActorId id, string folk, Ancestry ancestry, ActorTraits traits, int houseId, bool isAdult, string faithId)
    {
        Id = id;
        Folk = folk;
        Ancestry = ancestry;
        Traits = traits;
        HouseId = houseId;
        IsAdult = isAdult;
        FaithId = faithId;
    }

    /// <summary>
    /// Creates one actor. Traits are rolled from <c>(seed, id)</c> against
    /// <paramref name="priors"/>; adulthood compares <paramref name="ageYears"/>
    /// against <paramref name="maturationAgeYears"/>. Takes the maturation age as a
    /// plain number (not a folk record) so this rank-1 slice never depends upward on Peoples.
    /// </summary>
    /// <param name="seed">World seed feeding the trait roll.</param>
    /// <param name="id">Actor id; must not be <see cref="ActorId.None"/>.</param>
    /// <param name="folk">Primary folk key.</param>
    /// <param name="ancestry">Lineage shares.</param>
    /// <param name="priors">Five trait weights in O, C, E, A, N order.</param>
    /// <param name="maturationAgeYears">Folk maturation age in years.</param>
    /// <param name="ageYears">Actor age in years.</param>
    /// <param name="houseId">House/clan id; 0 means houseless.</param>
    /// <param name="faithId">Faith id; empty means none.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is none or <paramref name="folk"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="ancestry"/> or <paramref name="priors"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An age is negative.</exception>
    public static Actor Create(int seed, ActorId id, string folk, Ancestry ancestry, TraitWeight[] priors,
        int maturationAgeYears, int ageYears, int houseId = 0, string faithId = "")
    {
        if (id.IsNone) throw new ArgumentException("Cannot create ActorId.None.", nameof(id));
        if (string.IsNullOrWhiteSpace(folk)) throw new ArgumentException("Folk must be non-blank.", nameof(folk));
        if (ancestry is null) throw new ArgumentNullException(nameof(ancestry));
        if (maturationAgeYears < 0) throw new ArgumentOutOfRangeException(nameof(maturationAgeYears), "Maturation age cannot be negative.");
        if (ageYears < 0) throw new ArgumentOutOfRangeException(nameof(ageYears), "Age cannot be negative.");
        return new Actor(id, folk, ancestry, ActorTraits.Roll(seed, id, priors),
            houseId, IsAdultAge(ageYears, maturationAgeYears), faithId ?? string.Empty);
    }

    /// <summary>True when <paramref name="ageYears"/> reached <paramref name="maturationAgeYears"/>.</summary>
    /// <param name="ageYears">Actor age in years.</param>
    /// <param name="maturationAgeYears">Folk maturation age in years.</param>
    public static bool IsAdultAge(int ageYears, int maturationAgeYears) => ageYears >= maturationAgeYears;

    /// <summary>
    /// The adult gate: marriages, fostering and other alliance contracts admit adults
    /// only. Throws for a child actor; adult actors pass silently.
    /// </summary>
    /// <param name="purpose">What the actor was offered for (used in the error message).</param>
    /// <exception cref="InvalidOperationException">The actor is not an adult.</exception>
    public void RequireAdult(string purpose)
    {
        if (!IsAdult) throw new InvalidOperationException($"{Id} is not an adult and cannot be {purpose}.");
    }
}
