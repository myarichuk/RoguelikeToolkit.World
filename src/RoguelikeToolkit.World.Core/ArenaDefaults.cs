using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Default arena construction. Uses the .NET runtime backend instead of raw
/// platform mmap: the P/Invoke backend fails with OutOfMemoryException on macOS,
/// which made every default-constructed layer/stage unusable there.
/// Sizes are modest; generation temporaries are kilobytes.
/// </summary>
public static class ArenaDefaults
{
    public static ArenaAllocator Create()
        => new ArenaAllocator((System.UIntPtr)65536, (System.UIntPtr)(64 * 1024 * 1024), NativeAllocatorBackend.DotNetUnmanaged);
}
