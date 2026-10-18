using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.MapEditor.Engine;

/// <summary>
/// The engine's extension point for history plugins. A host application (a custom build of this engine)
/// registers plugins here before the first <c>Generate</c>; every generated world then runs them through
/// <see cref="WorldBuilder.WithHistoryScalars"/> and <see cref="WorldBuilder.WithHistoryDriver"/>. With
/// nothing registered the engine generates exactly the base history.
/// </summary>
public static class HistoryPluginHost
{
    private static readonly List<(Action<ScalarRegistry>? Scalars, Action<HistoryDriver> Operators)> Plugins = new();

    /// <summary>Adds a plugin: optional scalar declarations and the operator registration.</summary>
    public static void Register(Action<HistoryDriver> operators, Action<ScalarRegistry>? scalars = null)
    {
        ArgumentNullException.ThrowIfNull(operators);
        Plugins.Add((scalars, operators));
    }

    /// <summary>Removes every registered plugin.</summary>
    public static void Clear() => Plugins.Clear();

    /// <summary>Applies every registered plugin to <paramref name="builder"/>; <c>Generate</c> calls this for each world.</summary>
    public static void Apply(WorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var (scalars, operators) in Plugins)
        {
            if (scalars is not null) builder.WithHistoryScalars(scalars);
            builder.WithHistoryDriver(operators);
        }
    }
}
