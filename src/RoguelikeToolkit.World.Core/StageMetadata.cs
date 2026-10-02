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
    public static int GetOrder(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage declared)
            return declared.Order;
        var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
            .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
        return attr?.Order ?? int.MaxValue;
    }

    public static Type[] GetReads(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage declared)
            return declared.Reads ?? Array.Empty<Type>();
        var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
            .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
        return attr?.Reads ?? Array.Empty<Type>();
    }

    public static Type[] GetReadsOptional(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage declared)
            return declared.ReadsOptional ?? Array.Empty<Type>();
        var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
            .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
        return attr?.ReadsOptional ?? Array.Empty<Type>();
    }

    public static Type[] GetWrites(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage declared)
            return declared.Writes ?? Array.Empty<Type>();
        var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
            .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
        return attr?.Writes ?? Array.Empty<Type>();
    }

    public static bool HasDeclaration(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage)
            return true;
        var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
            .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
        return attr != null;
    }

    public static string DisplayName(IWorldGeneratorStage stage)
    {
        if (stage is IDeclaredStage declared && !string.IsNullOrWhiteSpace((declared as IStageNamed)?.Name))
            return ((IStageNamed)declared).Name!;
        return stage.GetType().Name;
    }
}

/// <summary>Optional friendly name for runtime stages (logs/diagnostics).</summary>
public interface IStageNamed
{
    string Name { get; }
}
