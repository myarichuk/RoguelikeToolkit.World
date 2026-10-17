namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Declared polity scalars. Ids are dense in declaration order and the built-ins are seeded first
/// (<see cref="Pressure"/> = 0), so two registries declared the same way are identical.
/// </summary>
public sealed class ScalarRegistry
{
    /// <summary>Id of the built-in crowding pressure scalar (0..<see cref="HistoryDemography.MaxPressure"/>).</summary>
    public const int Pressure = 0;

    /// <summary>Shared built-ins, used by polities that are not bound to a catalog registry.</summary>
    internal static readonly ScalarRegistry Builtin = new();

    private readonly List<ScalarDef> _defs = new();
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    /// <summary>Creates a registry seeded with the built-in scalars.</summary>
    public ScalarRegistry() => Declare("pressure", 0.0, HistoryDemography.MaxPressure, 0.0);

    /// <summary>Number of declared scalars.</summary>
    public int Count => _defs.Count;

    /// <summary>The definition with <paramref name="id"/>.</summary>
    public ScalarDef this[int id] => _defs[id];

    /// <summary>Declares a scalar, or returns the existing one when the name and range match.</summary>
    public ScalarDef Declare(string name, double min, double max, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (!(min <= max)) throw new ArgumentException("Min must not exceed max.", nameof(min));
        double def0 = Math.Clamp(defaultValue, min, max);
        if (_ids.TryGetValue(name, out int existing))
        {
            var old = _defs[existing];
            if (old.Min == min && old.Max == max && old.Default == def0) return old;
            throw new InvalidOperationException($"Scalar '{name}' is already declared with a different range.");
        }
        var def = new ScalarDef(_defs.Count, name, min, max, def0);
        _ids[name] = def.Id;
        _defs.Add(def);
        return def;
    }

    /// <summary>Declares a plugin scalar; the name must be <c>namespace/name</c> so it cannot shadow a built-in.</summary>
    public ScalarDef DeclarePlugin(string name, double min, double max, double defaultValue)
    {
        HistoryNaming.RequirePluginName(name, nameof(name));
        return Declare(name, min, max, defaultValue);
    }

    /// <summary>Looks up a scalar by name.</summary>
    public bool TryGet(string name, out ScalarDef def)
    {
        if (_ids.TryGetValue(name, out int id)) { def = _defs[id]; return true; }
        def = null!;
        return false;
    }
}
