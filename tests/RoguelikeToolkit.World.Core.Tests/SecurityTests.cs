using System;
using System.IO;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

public class SecurityTests : IDisposable
{
    private struct DummyData
    {
        public int Value;
    }

    [Fact]
    public void WorldDataStore_Constructor_WithValidPath_CreatesSuccessfully()
    {
        var validPath = "valid_test_store.bin";
        try
        {
            using var store = new WorldDataStore(1, validPath);
            store.RegisterLayer<DummyData>();
            store.Allocate();
            Assert.True(File.Exists(validPath));
        }
        finally
        {
            if (File.Exists(validPath))
            {
                File.Delete(validPath);
            }
        }
    }

    [Fact]
    public void WorldDataStore_Constructor_WithDirectoryTraversal_ThrowsUnauthorizedAccessException()
    {
        // Path that attempts to traverse up to a potentially restricted area
        var traversalPath = Path.Combine("..", "..", "..", "system32", "test_store.bin");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var store = new WorldDataStore(1, traversalPath);
            store.RegisterLayer<DummyData>();
            store.Allocate();
        });
    }

    [Fact]
    public void WorldDataStore_Constructor_WithAbsolutePathOutsideCurrentDir_ThrowsUnauthorizedAccessException()
    {
        // Windows absolute path
        var absolutePath = @"C:\Windows\System32\test_store.bin";

        // If running on Linux/Mac, test a root path
        if (!OperatingSystem.IsWindows())
        {
            absolutePath = "/etc/passwd"; // Just a system path, doesn't mean we have write access, but path check should block it first
        }

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var store = new WorldDataStore(1, absolutePath);
            store.RegisterLayer<DummyData>();
            store.Allocate();
        });
    }

    public void Dispose()
    {
        // Cleanup happens in the test finally blocks
    }
}
