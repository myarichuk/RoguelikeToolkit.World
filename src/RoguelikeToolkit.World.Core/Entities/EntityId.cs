namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>
/// Stable handle to an entity: a slot index plus a generation. The generation is
/// odd while the slot is alive and even while it is free, so a handle to a destroyed
/// (or recycled) entity can never alias a new one. <c>default</c> is "no entity".
/// </summary>
public readonly record struct EntityId(int Index, uint Generation)
{
    /// <summary>The null handle, <c>default(EntityId)</c>. It is never alive, so <see cref="EntityStore.IsAlive"/> is false for it.</summary>
    public static EntityId None => default;

    /// <summary>True for a handle with generation 0, which is what <c>default</c> has; no live entity ever carries generation 0.</summary>
    public bool IsNone => Generation == 0;

    /// <summary>"Entity(none)" for <see cref="None"/>, otherwise <c>Entity({Index}v{Generation})</c>.</summary>
    public override string ToString() => IsNone ? "Entity(none)" : $"Entity({Index}v{Generation})";
}
