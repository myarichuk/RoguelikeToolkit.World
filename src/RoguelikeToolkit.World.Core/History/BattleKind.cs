namespace RoguelikeToolkit.World.Core.History;

/// <summary>How an engagement was fought.</summary>
public enum BattleKind : byte
{
    /// <summary>Two hosts met in the open.</summary>
    Field = 0,
    /// <summary>One host caught the other unready.</summary>
    Ambush = 1,
    /// <summary>A walled settlement was invested.</summary>
    Siege = 2,
    /// <summary>A host marched to break a siege.</summary>
    Relief = 3,
    /// <summary>Fleets met at sea.</summary>
    Naval = 4,
}
