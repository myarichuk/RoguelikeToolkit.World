namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Reserved ranges and names for history plugins. Built-in operators sit below
/// <see cref="PluginOrderMin"/> and use bare names; a plugin's operators, scalars and state keys use
/// <c>namespace/name</c> and its operators run inside <see cref="PluginOrderMin"/>..<see cref="PluginOrderMax"/>.
/// Order only decides when <c>Propose</c> runs, never outcomes, so the band is a convention that keeps
/// plugins from colliding with Core, not a determinism rule.
/// </summary>
public static class HistoryNaming
{
    /// <summary>First order value reserved for plugin operators (built-ins stay below 100).</summary>
    public const int PluginOrderMin = 100;

    /// <summary>Last order value reserved for plugin operators.</summary>
    public const int PluginOrderMax = 899;

    /// <summary>Separator between a plugin's namespace and a name inside it.</summary>
    public const char NamespaceSeparator = '/';

    /// <summary>True when <paramref name="name"/> is <c>namespace/name</c> with both parts non-empty and a single separator.</summary>
    public static bool IsPluginName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        int i = name!.IndexOf(NamespaceSeparator);
        return i > 0 && i < name.Length - 1 && name.IndexOf(NamespaceSeparator, i + 1) < 0 && name.Trim() == name;
    }

    /// <summary>The namespace part of a plugin name, or the whole string when it has no separator.</summary>
    public static string NamespaceOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int i = name.IndexOf(NamespaceSeparator);
        return i < 0 ? name : name.Substring(0, i);
    }

    /// <summary>Throws unless <paramref name="name"/> is a plugin name (<c>namespace/name</c>).</summary>
    public static void RequirePluginName(string? name, string paramName)
    {
        if (!IsPluginName(name))
            throw new ArgumentException($"Plugin names must look like 'namespace/name', got '{name}'.", paramName);
    }

    /// <summary>Throws unless <paramref name="order"/> lies in the plugin band.</summary>
    public static void RequirePluginOrder(int order, string paramName)
    {
        if (order < PluginOrderMin || order > PluginOrderMax)
            throw new ArgumentOutOfRangeException(paramName, order, $"Plugin operator order must be {PluginOrderMin}..{PluginOrderMax}.");
    }
}
