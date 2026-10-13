namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>Hex adjacency for <see cref="PopulationGrid.Step{TPolicy,TAdj}"/>. Implement as a struct so the call inlines.</summary>
public interface IAdjacency
{
    /// <summary>Writes the neighbouring cells of <paramref name="cell"/> (at most 8) and returns how many.</summary>
    int GetAdjacent(int cell, Span<int> neighbors);
}
