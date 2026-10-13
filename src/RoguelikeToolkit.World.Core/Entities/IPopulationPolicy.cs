namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>
/// The game's rules for aggregate population change. All rates are per tick and are clamped to [0, 1]
/// by the grid. Implement as a struct so the calls inline.
/// </summary>
public interface IPopulationPolicy
{
    /// <summary>Births this tick as a fraction of <paramref name="count"/>, the current head count of <paramref name="group"/> in <paramref name="cell"/>. Clamped to [0, 1] (NaN counts as 0); the expected number is stochastically rounded. Only called for non-empty groups.</summary>
    double BirthRate(int cell, int group, uint count);
    /// <summary>Deaths this tick as a fraction of <paramref name="count"/>, the current head count of <paramref name="group"/> in <paramref name="cell"/>. Clamped to [0, 1] (NaN counts as 0). Deaths are resolved before migration, so only survivors can move. Only called for non-empty groups.</summary>
    double DeathRate(int cell, int group, uint count);

    /// <summary>Fraction of the group that leaves the cell this tick.</summary>
    double MigrationRate(int cell, int group, uint count);

    /// <summary>How appealing <paramref name="cell"/> is as a destination for <paramref name="group"/>; zero or less = never.</summary>
    double Attraction(int cell, int group);
}
