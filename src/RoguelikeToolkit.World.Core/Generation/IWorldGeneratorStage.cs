using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>One step of world generation. A stage reads and writes the field layers of a <see cref="WorldMap"/>'s store; its order and layer contract come from <see cref="WorldGeneratorStageAttribute"/> or <see cref="IDeclaredStage"/>.</summary>
public interface IWorldGeneratorStage
{
    /// <summary>Runs the stage against <paramref name="map"/>, writing its output layers in place.</summary>
    void Execute(WorldMap map);
}
