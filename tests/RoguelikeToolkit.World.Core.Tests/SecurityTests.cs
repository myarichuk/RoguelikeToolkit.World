using System;
using System.IO;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class SecurityTests
{
    private struct DummyData
    {
        public int Value;
    }

    [Fact]
    public void HexSphereStore_Constructor_WithValidPath_CreatesSuccessfully()
    {
        string validPath = "test_map_data.dat";

        try
        {
            using var store = new HexSphereStore<DummyData>(1, validPath);
            Assert.NotNull(store);
            Assert.Equal(1, store.Size);
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
    public void HexSphereStore_Constructor_WithDirectoryTraversal_ThrowsUnauthorizedAccessException()
    {
        // Path traversing up from current directory
        string traversalPath = Path.Combine("..", "test_map_data.dat");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var store = new HexSphereStore<DummyData>(1, traversalPath);
        });
    }

    [Fact]
    public void HexSphereStore_Constructor_WithAbsolutePathOutsideCurrentDir_ThrowsUnauthorizedAccessException()
    {
        // Create an absolute path clearly outside the current working directory
        // Using the temp directory which should not be the current working directory
        string absolutePath = Path.Combine(Path.GetTempPath(), "test_map_data.dat");

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var store = new HexSphereStore<DummyData>(1, absolutePath);
        });
    }
}
