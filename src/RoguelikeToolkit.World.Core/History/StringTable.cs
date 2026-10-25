namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Interning table for history identifiers (event kinds today; scalar and role names later). Ids are
/// dense and assigned in first-seen order, so a table rebuilt from the same sequence is identical.
/// The built-in <see cref="HistoryEventKind"/> names are seeded first, which keeps their ids equal to
/// the enum values.
/// </summary>
public sealed class StringTable
{
    private readonly List<string> _names = new();
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    /// <summary>Creates a table seeded with the built-in event kind names (id == enum value).</summary>
    public StringTable() => SeedBuiltIns();

    /// <summary>Drops every interned name and re-seeds the built-ins, as a new table would be.</summary>
    internal void Clear()
    {
        _names.Clear();
        _ids.Clear();
        SeedBuiltIns();
    }

    private void SeedBuiltIns()
    {
        foreach (var kind in Enum.GetValues<HistoryEventKind>().OrderBy(k => (byte)k))
        {
            int id = Intern(kind.ToString());
            if (id != (byte)kind) throw new InvalidOperationException("Built-in event kinds must be dense from zero.");
        }
    }

    /// <summary>Number of interned names.</summary>
    public int Count => _names.Count;

    /// <summary>Returns the id for <paramref name="name"/>, adding it when new.</summary>
    public int Intern(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (_ids.TryGetValue(name, out int id)) return id;
        id = _names.Count;
        _names.Add(name);
        _ids[name] = id;
        return id;
    }

    /// <summary>Returns the id of an already interned name, or false.</summary>
    public bool TryGetId(string name, out int id) => _ids.TryGetValue(name, out id);

    /// <summary>Returns the name for <paramref name="id"/>.</summary>
    public string NameOf(int id)
        => (uint)id < (uint)_names.Count ? _names[id] : throw new ArgumentOutOfRangeException(nameof(id));
}
