namespace RoguelikeToolkit.World.Core.Actors;

/// <summary>
/// Stable handle to a simulated actor. Actor ids are 1-based: <c>default</c> is
/// "no actor" (<see cref="None"/>), so a zero id can never alias a live actor.
/// </summary>
public readonly record struct ActorId(int Value)
{
    /// <summary>The null handle, <c>default(ActorId)</c>. It is never a live actor.</summary>
    public static ActorId None => default;

    /// <summary>True for a handle with value 0, which is what <c>default</c> has; no live actor ever carries id 0.</summary>
    public bool IsNone => Value == 0;

    /// <summary>"Actor(none)" for <see cref="None"/>, otherwise <c>Actor({Value})</c>.</summary>
    public override string ToString() => IsNone ? "Actor(none)" : $"Actor({Value})";
}
