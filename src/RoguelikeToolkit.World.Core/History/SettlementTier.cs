namespace RoguelikeToolkit.World.Core.History;

/// <summary>Size class of a historical settlement; capitals start as <see cref="City"/>.</summary>
public enum SettlementTier
{
    /// <summary>A handful of households.</summary>
    Hamlet = 0,
    /// <summary>A farming or herding village.</summary>
    Village = 1,
    /// <summary>A market town.</summary>
    Town = 2,
    /// <summary>A city, the seat of a polity or a regional centre.</summary>
    City = 3,
}
