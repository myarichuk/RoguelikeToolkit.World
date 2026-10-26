using System.Text;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Saved string tables are remapped by name on load. A file written before an event kind was added has a shorter
/// built-in prefix (no PolityAbsorbed / Plugin), so the slots this build reserves for those kinds hold the file's own
/// names (end causes); requiring the ids to line up made every such save unloadable.
/// </summary>
public class HistoryStringTableLoadTests
{
    private static (HistoryCatalog History, byte[] Bytes, List<string> Table) Saved()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(5).Build();
        var history = world.History;
        Assert.NotEmpty(history.Polities);
        history.Polities[0].EndedTurn = 1;
        history.Polities[0].EndCause = history.Strings.Intern("collapse-test");
        var table = Enumerable.Range(0, history.Strings.Count).Select(history.Strings.NameOf).ToList();
        return (history, history.ToBytes(), table);
    }

    /// <summary>Replaces the saved string table with <paramref name="names"/> (same count, so every stored id keeps its slot).</summary>
    private static byte[] WithTable(byte[] bytes, IReadOnlyList<string> table, IReadOnlyList<string> names)
    {
        Assert.Equal(table.Count, names.Count);
        int at = IndexOf(bytes, BitConverter.GetBytes(table.Count).Concat(Encode(table[0])).ToArray());
        Assert.True(at >= 0, "string table not found");

        using var r = new BinaryReader(new MemoryStream(bytes, at, bytes.Length - at));
        int original = r.ReadInt32();
        for (int i = 0; i < original; i++) r.ReadString();
        int end = at + (int)r.BaseStream.Position;

        using var ms = new MemoryStream();
        ms.Write(bytes, 0, at);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(names.Count);
            foreach (var n in names) w.Write(n);
        }
        ms.Write(bytes, end, bytes.Length - end);
        return ms.ToArray();
    }

    private static byte[] Encode(string s)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) w.Write(s);
        return ms.ToArray();
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    private static string? CauseName(HistoryCatalog h, Polity p) => p.EndCause < 0 ? null : h.Strings.NameOf(p.EndCause);

    [Fact]
    public void CurrentSave_RoundTripsByteForByte()
    {
        var (_, bytes, _) = Saved();
        Assert.Equal(bytes, HistoryCatalog.Load(new MemoryStream(bytes)).ToBytes());
    }

    [Fact]
    public void SaveWithShorterBuiltInPrefix_LoadsAndKeepsEndCauses()
    {
        var (history, bytes, table) = Saved();
        // An older build: the slots now taken by PolityAbsorbed and Plugin held that file's own names.
        var names = table.ToList();
        names[(int)HistoryEventKind.PolityAbsorbed] = "legacy-a";
        names[(int)HistoryEventKind.Plugin] = "legacy-b";

        var loaded = HistoryCatalog.Load(new MemoryStream(WithTable(bytes, table, names)));

        Assert.Equal(history.Polities.Select(p => CauseName(history, p)), loaded.Polities.Select(p => CauseName(loaded, p)));
        Assert.Equal("collapse-test", CauseName(loaded, loaded.Polities[0]));
        Assert.Equal(nameof(HistoryEventKind.Plugin), loaded.Strings.NameOf((int)HistoryEventKind.Plugin));
    }

    [Fact]
    public void DuplicateNames_AreCorrupt()
    {
        var (_, bytes, table) = Saved();
        var names = table.ToList();
        names[^1] = names[0];
        Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(new MemoryStream(WithTable(bytes, table, names))));
    }
}
