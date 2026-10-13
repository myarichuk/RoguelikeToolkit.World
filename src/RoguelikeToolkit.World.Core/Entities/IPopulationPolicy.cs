using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>
/// The game's rules for aggregate population change. All rates are per tick and are clamped to [0, 1]
/// by the grid. Implement as a struct so the calls inline.
/// </summary>
public interface IPopulationPolicy
{
    double BirthRate(int cell, int group, uint count);
    double DeathRate(int cell, int group, uint count);

    /// <summary>Fraction of the group that leaves the cell this tick.</summary>
    double MigrationRate(int cell, int group, uint count);

    /// <summary>How appealing <paramref name="cell"/> is as a destination for <paramref name="group"/>; zero or less = never.</summary>
    double Attraction(int cell, int group);
}
