using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>The store's containment check must judge where a path really lands, not how it is spelled.</summary>
public sealed class StorePathSymlinkTests : IDisposable
{
    private struct Dummy { public int Value; }

    private readonly string _inside = Path.Combine(Environment.CurrentDirectory, "symlink-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "symlink-outside-" + Guid.NewGuid().ToString("N"));

    public StorePathSymlinkTests()
    {
        Directory.CreateDirectory(_inside);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        try { Directory.Delete(_inside, true); } catch (IOException) { }
        try { Directory.Delete(_outside, true); } catch (IOException) { }
    }

    private static void Allocate(string path)
    {
        using var store = new WorldDataStore(1, path);
        store.RegisterLayer<Dummy>();
        store.Allocate();
    }

    // Creating a link can need privileges (Windows without developer mode): skip rather than fail there.
    private static bool TryLinkDirectory(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return false; }
    }

    [Fact]
    public void ADirectoryLinkPointingOutside_IsRejected()
    {
        var link = Path.Combine(_inside, "escape");
        if (!TryLinkDirectory(link, _outside)) return;

        Assert.Throws<UnauthorizedAccessException>(() => Allocate(Path.Combine(link, "store.bin")));
        Assert.False(File.Exists(Path.Combine(_outside, "store.bin")), "the store file must not be created outside the directory");
    }

    [Fact]
    public void AFileLinkPointingOutside_IsRejected()
    {
        var target = Path.Combine(_outside, "victim.bin");
        File.WriteAllBytes(target, new byte[16]);
        var link = Path.Combine(_inside, "store.bin");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }

        Assert.Throws<UnauthorizedAccessException>(() => Allocate(link));
        Assert.Equal(16, new FileInfo(target).Length);   // untouched: not resized, not truncated
    }

    [Fact]
    public void AChainOfLinks_IsFollowedToItsEnd()
    {
        var hop = Path.Combine(_inside, "hop");
        var escape = Path.Combine(_inside, "escape");
        if (!TryLinkDirectory(escape, _outside) || !TryLinkDirectory(hop, escape)) return;

        Assert.Throws<UnauthorizedAccessException>(() => Allocate(Path.Combine(hop, "store.bin")));
    }

    [Fact]
    public void ALinkThatStaysInside_IsAllowed()
    {
        var real = Path.Combine(_inside, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_inside, "alias");
        if (!TryLinkDirectory(link, real)) return;

        Allocate(Path.Combine(link, "store.bin"));
        Assert.True(File.Exists(Path.Combine(real, "store.bin")));
    }
}
