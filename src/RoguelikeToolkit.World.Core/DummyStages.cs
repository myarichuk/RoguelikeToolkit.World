using System;

namespace RoguelikeToolkit.World.Core;

// Dummy stages to keep pipeline discovery tests working without spiral code.
[WorldGeneratorStage(100)]
public class DummyStage1 : IWorldGeneratorStage
{
    public void Execute(WorldMap map) { }
}

[WorldGeneratorStage(200)]
public class DummyStage2 : IWorldGeneratorStage
{
    public void Execute(WorldMap map) { }
}
