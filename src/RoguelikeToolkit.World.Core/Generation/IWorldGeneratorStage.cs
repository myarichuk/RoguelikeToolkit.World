namespace RoguelikeToolkit.World.Core;

public interface IWorldGeneratorStage
{
    void Execute(WorldMap map);
}
