using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.11: the plugin state bag round-trips, survives Respatialize and reports version mismatches.</summary>
public class PluginStateBagTests
{
    private static World Build(int seed = 42)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();

    private static HistoryCatalog RoundTrip(HistoryCatalog h)
    {
        using var stream = new MemoryStream(h.ToBytes(), writable: false);
        return HistoryCatalog.Load(stream);
    }

    [Fact]
    public void Blobs_RoundTrip_ByteForByte_InKeyOrder()
    {
        using var w = Build();
        var bag = w.History.PluginState;
        bag.Set("econ/ledger", 2, new byte[] { 1, 2, 3 });
        bag.Set("econ/a", 1, Array.Empty<byte>());
        bag.Reset("econ/old", 3, new byte[] { 9 }, "format changed");

        var loaded = RoundTrip(w.History);
        Assert.Equal(w.History.ToBytes(), loaded.ToBytes());
        Assert.Equal(new[] { "econ/a", "econ/ledger", "econ/old" }, loaded.PluginState.Keys);
        Assert.Equal(PluginStateLookup.Found, loaded.PluginState.TryRead("econ/ledger", 2, out var data));
        Assert.Equal(new byte[] { 1, 2, 3 }, data);
        Assert.Single(loaded.PluginState.Notes);
        Assert.Contains("format changed", loaded.PluginState.Notes[0]);
    }

    [Fact]
    public void Mismatch_IsReported_AndResetLeavesANote()
    {
        using var w = Build();
        var bag = w.History.PluginState;
        Assert.Equal(PluginStateLookup.Missing, bag.TryRead("k", 1, out _));
        bag.Set("k", 1, new byte[] { 5 });
        Assert.Equal(PluginStateLookup.VersionMismatch, bag.TryRead("k", 2, out var none));
        Assert.Empty(none);
        bag.Reset("k", 2, new byte[] { 6 }, "v1 unsupported");
        Assert.Equal(PluginStateLookup.Found, bag.TryRead("k", 2, out var data));
        Assert.Equal(new byte[] { 6 }, data);
        Assert.Equal("k: reset v1 -> v2: v1 unsupported", bag.Notes[0]);
    }

    [Fact]
    public void Blobs_AreCopied_OnSetAndRead()
    {
        using var w = Build();
        var src = new byte[] { 1, 2 };
        w.History.PluginState.Set("k", 1, src);
        src[0] = 99;
        w.History.PluginState.TryRead("k", 1, out var a);
        a[1] = 99;
        w.History.PluginState.TryRead("k", 1, out var b);
        Assert.Equal(new byte[] { 1, 2 }, b);
    }

    [Fact]
    public void Blobs_SurviveRespatialize()
    {
        using var w = Build();
        w.History.PluginState.Set("k", 1, new byte[] { 7 });
        w.History.Respatialize(w.Map.DataStore);
        Assert.Equal(PluginStateLookup.Found, w.History.PluginState.TryRead("k", 1, out var data));
        Assert.Equal(new byte[] { 7 }, data);
    }

    [Fact]
    public void Load_RejectsTruncatedBlob()
    {
        using var w = Build();
        w.History.PluginState.Set("k", 1, new byte[64]);
        byte[] bytes = w.History.ToBytes();
        Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(new MemoryStream(bytes[..^20])));
    }

    [Fact]
    public void Keys_AreValidated()
    {
        using var w = Build();
        Assert.Throws<ArgumentException>(() => w.History.PluginState.Set(" ", 1, default));
        Assert.Throws<ArgumentException>(() => w.History.PluginState.Set(new string('x', PluginStateBag.MaxKeyLength + 1), 1, default));
    }
}
