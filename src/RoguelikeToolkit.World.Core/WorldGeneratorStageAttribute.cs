using System;

namespace RoguelikeToolkit.World.Core;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WorldGeneratorStageAttribute : Attribute
{
    public int Order { get; }

    public WorldGeneratorStageAttribute(int order)
    {
        Order = order;
    }
}
