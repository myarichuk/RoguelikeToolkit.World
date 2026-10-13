using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Entities;

/// <summary>Adjacency over a planet's tile graph.</summary>
public readonly struct WorldAdjacency(WorldDataStore store) : IAdjacency
{
    public int GetAdjacent(int cell, Span<int> neighbors) => store.GetAdjacent(cell, neighbors);
}
