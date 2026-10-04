namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>
/// Stable handle to an entity: a slot index plus a generation. The generation is
/// odd while the slot is alive and even while it is free, so a handle to a destroyed
/// (or recycled) entity can never alias a new one. <c>default</c> is "no entity".
/// </summary>
public readonly record struct EntityId(int Index, uint Generation)
{
    public static EntityId None => default;

    public bool IsNone => Generation == 0;

    public override string ToString() => IsNone ? "Entity(none)" : $"Entity({Index}v{Generation})";
}
