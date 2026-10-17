namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Keyed, versioned binary blobs a history plugin keeps with the catalog. Saved and loaded with it,
/// untouched by <see cref="HistoryCatalog.Respatialize"/>, and opaque to Core. A plugin owns its keys
/// (prefix them with the plugin name) and its blob format; a version mismatch is reported, never
/// guessed, and a reset leaves a note behind.
/// </summary>
public sealed class PluginStateBag
{
    /// <summary>Longest accepted key, in characters.</summary>
    public const int MaxKeyLength = 128;

    private readonly SortedDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _notes = new();

    /// <summary>Keys in ordinal order (the save order).</summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>Number of stored blobs.</summary>
    public int Count => _entries.Count;

    /// <summary>Human-readable records of resets, in the order they happened; persisted.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Stores a copy of <paramref name="data"/> under <paramref name="key"/>, replacing any earlier blob.</summary>
    public void Set(string key, int version, ReadOnlySpan<byte> data)
    {
        CheckKey(key);
        _entries[key] = new Entry(version, data.ToArray());
    }

    /// <summary>Reads the blob under <paramref name="key"/> when it was written at <paramref name="expectedVersion"/>.</summary>
    public PluginStateLookup TryRead(string key, int expectedVersion, out byte[] data)
    {
        CheckKey(key);
        data = Array.Empty<byte>();
        if (!_entries.TryGetValue(key, out var entry)) return PluginStateLookup.Missing;
        if (entry.Version != expectedVersion) return PluginStateLookup.VersionMismatch;
        data = (byte[])entry.Data.Clone();
        return PluginStateLookup.Found;
    }

    /// <summary>Version of the blob under <paramref name="key"/>, or null when absent (for plugin migrations).</summary>
    public int? VersionOf(string key)
    {
        CheckKey(key);
        return _entries.TryGetValue(key, out var entry) ? entry.Version : null;
    }

    /// <summary>Removes the blob under <paramref name="key"/>; false when none was stored.</summary>
    public bool Remove(string key)
    {
        CheckKey(key);
        return _entries.Remove(key);
    }

    /// <summary>Replaces the blob under <paramref name="key"/> and records <paramref name="reason"/> as a note.</summary>
    public void Reset(string key, int version, ReadOnlySpan<byte> data, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reset needs a reason.", nameof(reason));
        int? old = VersionOf(key);
        Set(key, version, data);
        _notes.Add($"{key}: reset {(old.HasValue ? $"v{old.Value}" : "absent")} -> v{version}: {reason}");
    }

    internal void AddNote(string note) => _notes.Add(note);

    internal IEnumerable<(string Key, int Version, byte[] Data)> Entries()
    {
        foreach (var kv in _entries) yield return (kv.Key, kv.Value.Version, kv.Value.Data);
    }

    private static void CheckKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Plugin state key must not be blank.", nameof(key));
        if (key.Length > MaxKeyLength) throw new ArgumentException($"Plugin state key longer than {MaxKeyLength} characters.", nameof(key));
    }

    private readonly record struct Entry(int Version, byte[] Data);
}
