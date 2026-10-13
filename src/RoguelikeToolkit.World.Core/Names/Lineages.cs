namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Which parent's line the name follows. Caller-overridable per call.</summary>
public static class Lineages
{
    /// <summary>The name follows the father's line (patronymic).</summary>
    public const string Patrilineal = "patrilineal";
    /// <summary>The name follows the mother's line (matronymic).</summary>
    public const string Matrilineal = "matrilineal";
    /// <summary>The name follows both lines; the generator builds the same parent slot as for <see cref="Patrilineal"/> (only <see cref="Matrilineal"/> changes the result).</summary>
    public const string Bilineal = "bilineal";
}
