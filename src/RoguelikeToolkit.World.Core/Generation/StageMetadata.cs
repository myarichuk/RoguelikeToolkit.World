using System;
using System.Linq;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Single place that resolves a stage's order and layer contracts.
/// <see cref="IDeclaredStage"/> (runtime/script stages) wins; otherwise the
/// <see cref="WorldGeneratorStageAttribute"/> applies; otherwise the stage is
/// treated as undeclared (order last, no contract validation).
/// </summary>
public static class StageMetadata
{
    private readonly record struct Declaration(
        bool IsDeclared, int Order, Type[] Reads, Type[] ReadsOptional, Type[] Writes);

    private static readonly Declaration Undeclared =
        new(false, int.MaxValue, Array.Empty<Type>(), Array.Empty<Type>(), Array.Empty<Type>());

    // The one place that knows the precedence: runtime declaration, then attribute, then nothing.
    private static Declaration Resolve(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage declared)
            return new Declaration(true, declared.Order,
                declared.Reads ?? Array.Empty<Type>(),
                declared.ReadsOptional ?? Array.Empty<Type>(),
                declared.Writes ?? Array.Empty<Type>());

        var attr = (WorldGeneratorStageAttribute?)Attribute.GetCustomAttribute(
            stage.GetType(), typeof(WorldGeneratorStageAttribute), inherit: false);
        return attr == null
            ? Undeclared
            : new Declaration(true, attr.Order,
                attr.Reads ?? Array.Empty<Type>(),
                attr.ReadsOptional ?? Array.Empty<Type>(),
                attr.Writes ?? Array.Empty<Type>());
    }

    public static int GetOrder(IWorldGeneratorStage stage) => Resolve(stage).Order;
    public static Type[] GetReads(IWorldGeneratorStage stage) => Resolve(stage).Reads;
    public static Type[] GetReadsOptional(IWorldGeneratorStage stage) => Resolve(stage).ReadsOptional;
    public static Type[] GetWrites(IWorldGeneratorStage stage) => Resolve(stage).Writes;
    public static bool HasDeclaration(IWorldGeneratorStage stage) => Resolve(stage).IsDeclared;

    public static string DisplayName(IWorldGeneratorStage stage)
        => stage is IStageNamed { Name: { } name } && !string.IsNullOrWhiteSpace(name)
            ? name
            : stage.GetType().Name;
}

/// <summary>Optional friendly name for runtime stages (logs/diagnostics).</summary>
public interface IStageNamed
{
    string Name { get; }
}
