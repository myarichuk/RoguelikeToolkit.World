namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// How a polity holds together. Seeded by <see cref="PolityFormStage"/> from land
/// capacity, water access and nearby deposits; later operators (war, conquest,
/// rebellion) move polities between collapse and transformation, never the form itself.
/// </summary>
public enum PolityForm : byte
{
    /// <summary>A centralized agrarian state: high-capacity farmland behind a defensible capital.</summary>
    Kingdom = 0,
    /// <summary>A coastal trade league: harbour capital near deposits, holding territory lightly.</summary>
    MerchantRepublic = 1,
    /// <summary>A loose association of settlements: no strong center, splits and merges cheaply.</summary>
    LooseAssociation = 2,
    /// <summary>A mobile pastoral confederation: pasture-rich, low-arable ground, ruling by movement.</summary>
    Horde = 3,
}
