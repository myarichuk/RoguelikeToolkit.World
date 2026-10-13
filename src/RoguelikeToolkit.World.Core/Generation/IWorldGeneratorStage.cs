using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Generation;

public interface IWorldGeneratorStage
{
    void Execute(WorldMap map);
}
