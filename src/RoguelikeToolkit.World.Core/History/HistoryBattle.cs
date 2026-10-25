namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One engagement (catalog list <see cref="HistoryCatalog.Battles"/>, id = index). A record rather than an event so it survives
/// log compaction; the War or Siege event that narrates it carries a <c>battle:{id}</c> tag and <see cref="EventId"/> points
/// back. Open vocabularies (terrain class, beats, siege end) are interned names.
/// </summary>
public sealed class HistoryBattle
{
    /// <summary>Index in <see cref="HistoryCatalog.Battles"/>.</summary>
    public int Id { get; init; }
    /// <summary>Turn of the engagement.</summary>
    public int Turn { get; init; }
    /// <summary>Year within the turn (0..TurnYears-1).</summary>
    public int Year { get; init; }
    /// <summary>Season it was fought in.</summary>
    public HistorySeason Season { get; init; }
    /// <summary>How it was fought.</summary>
    public BattleKind Kind { get; init; }
    /// <summary>Planet tile of the field (or the besieged settlement).</summary>
    public int Tile { get; init; } = -1;
    /// <summary>Interned terrain class of the field (<c>open</c>, <c>forest</c>, <c>hills</c>...), or -1.</summary>
    public int Terrain { get; init; } = -1;
    /// <summary>Index into <see cref="HistoryCatalog.Settlements"/> of the besieged settlement, or -1.</summary>
    public int Settlement { get; init; } = -1;
    /// <summary>The attacking side.</summary>
    public BattleSide Attacker { get; init; } = new();
    /// <summary>The defending side.</summary>
    public BattleSide Defender { get; init; } = new();
    /// <summary>How it ended.</summary>
    public BattleOutcome Outcome { get; init; }
    /// <summary>Interned beat tags in the order they happened (<c>flank</c>, <c>feigned_retreat</c>, <c>general_fell</c>...).</summary>
    public List<int> Beats { get; } = new();
    /// <summary>Months a siege lasted (0 for a field battle).</summary>
    public int SiegeMonths { get; init; }
    /// <summary>Interned siege end (<c>stormed</c>, <c>surrendered</c>, <c>betrayed</c>, <c>relieved</c>, <c>lifted</c>...), or -1.</summary>
    public int SiegeEnd { get; init; } = -1;
    /// <summary>Battle id of the siege a relief battle broke into, or -1.</summary>
    public int Parent { get; init; } = -1;
    /// <summary>Id of the event that narrates the battle, or -1 when the event budget dropped it.</summary>
    public int EventId { get; set; } = -1;
}
