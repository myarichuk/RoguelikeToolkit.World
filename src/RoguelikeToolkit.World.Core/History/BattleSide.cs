namespace RoguelikeToolkit.World.Core.History;

/// <summary>One side of a <see cref="HistoryBattle"/>. Tactic and unit are interned names from the warfare data.</summary>
public sealed class BattleSide
{
    /// <summary>Polity id fighting on this side.</summary>
    public int Polity { get; init; } = -1;
    /// <summary>Person id of the commander, or -1.</summary>
    public int General { get; init; } = -1;
    /// <summary>Fighting strength brought to the field (levy units, finite, non-negative).</summary>
    public double Strength { get; init; }
    /// <summary>Share of <see cref="Strength"/> lost, 0..1.</summary>
    public double LossShare { get; init; }
    /// <summary>Supply level at contact, 0..1.</summary>
    public double Supply { get; init; }
    /// <summary>Morale at contact (about 0.2..1.2).</summary>
    public double Morale { get; init; }
    /// <summary>Interned tactic the side fought with, or -1.</summary>
    public int Tactic { get; init; } = -1;
    /// <summary>Interned signature unit, or -1.</summary>
    public int Unit { get; init; } = -1;
}
