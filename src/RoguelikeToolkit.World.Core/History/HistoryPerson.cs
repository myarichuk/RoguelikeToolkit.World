using RoguelikeToolkit.World.Core.Actors;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// A named person of the history (catalog list <see cref="HistoryCatalog.Persons"/>, id = index). Open vocabularies (death
/// causes, epithets) are interned names in the catalog's <see cref="StringTable"/>, so data can add them without code.
/// Every draw about a person keys on <see cref="StableKey"/>, never on <see cref="Id"/>: ids shift when a plugin adds
/// persons, outcomes must not.
/// </summary>
public sealed class HistoryPerson
{
    /// <summary>Index in <see cref="HistoryCatalog.Persons"/>.</summary>
    public int Id { get; init; }
    /// <summary>The person's role in the story.</summary>
    public PersonKind Kind { get; set; }
    /// <summary>Folk key (as on <see cref="Polity.FolkKey"/>).</summary>
    public string FolkKey { get; init; } = string.Empty;
    /// <summary>Culture key (as on <see cref="Polity.CultureKey"/>).</summary>
    public string CultureKey { get; init; } = string.Empty;
    /// <summary>Polity the person is loyal to, or -1 (a free outlaw).</summary>
    public int PolityId { get; set; } = -1;
    /// <summary>Index into <see cref="HistoryCatalog.Settlements"/> of the person's seat, or -1.</summary>
    public int SeatSettlement { get; set; } = -1;
    /// <summary>Person id of the founder of the person's house, or -1.</summary>
    public int HouseId { get; init; } = -1;
    /// <summary>Person id of a parent, or -1.</summary>
    public int Parent { get; init; } = -1;
    /// <summary>Grammatical gender for names and prose.</summary>
    public bool Feminine { get; init; }
    /// <summary>Turn the person was born on (may precede the history: an adult founder's birth turn is negative).</summary>
    public int BornTurn { get; init; }
    /// <summary>Year within <see cref="BornTurn"/> (0..TurnYears-1).</summary>
    public int BornYear { get; init; }
    /// <summary>Turn the person died on, or -1 while alive.</summary>
    public int DiedTurn { get; set; } = -1;
    /// <summary>Interned death cause (<c>age</c>, <c>battle</c>, <c>executed</c>...), or -1 while alive.</summary>
    public int DeathCause { get; set; } = -1;
    /// <summary>Personality (OCEAN bytes, 0..255; the court data reads them on a 0-100 scale).</summary>
    public ActorTraits Traits { get; init; }
    /// <summary>Command in the field, 0..100.</summary>
    public byte Martial { get; init; }
    /// <summary>Plots and spycraft, 0..100.</summary>
    public byte Intrigue { get; init; }
    /// <summary>Running a household or a realm, 0..100.</summary>
    public byte Stewardship { get; init; }
    /// <summary>Swaying people, 0..100.</summary>
    public byte Charisma { get; init; }
    /// <summary>Interned epithet ids (0..3), from the traits data.</summary>
    public List<int> Epithets { get; } = new();
    /// <summary>Fame, earned in battles, raids and office.</summary>
    public int Renown { get; set; }
    /// <summary>Private wealth (merchant princes, outlaws), in polity population units.</summary>
    public double Wealth { get; set; }
    /// <summary>Loyalty to the ruler, -100..100.</summary>
    public sbyte Loyalty { get; set; }
    /// <summary>War funding pledged by a merchant, consumed by the next campaign.</summary>
    public double Pledged { get; set; }
    /// <summary>The key every draw about the person derives from (see <see cref="ProposalSink.StableKey"/>).</summary>
    public int StableKey { get; init; }
    /// <summary>True while the person lives.</summary>
    public bool Alive => DiedTurn < 0;
}
