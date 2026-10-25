namespace RoguelikeToolkit.World.Core.History;

/// <summary>How an engagement ended, from the attacker's side.</summary>
public enum BattleOutcome : byte
{
    /// <summary>The defender was routed and pursued.</summary>
    AttackerDecisive = 0,
    /// <summary>The attacker held the field.</summary>
    AttackerVictory = 1,
    /// <summary>The attacker won at a cost it could not afford.</summary>
    AttackerPyrrhic = 2,
    /// <summary>Neither side broke.</summary>
    Stalemate = 3,
    /// <summary>The defender held.</summary>
    DefenderVictory = 4,
    /// <summary>The attacker was routed and pursued.</summary>
    DefenderDecisive = 5,
    /// <summary>No battle: the campaign failed before contact (supply, weather, a declined field).</summary>
    Averted = 6,
}
