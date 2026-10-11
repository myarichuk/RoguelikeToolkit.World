using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Default arena construction. Sizes are modest; generation temporaries are kilobytes.
/// </summary>
public static class ArenaDefaults
{
    public static ArenaAllocator Create()
#if NET
        => new ArenaAllocator((System.UIntPtr)65536, (System.UIntPtr)(64 * 1024 * 1024));
#else
        // Unity (Mono/IL2CPP): the default backend P/Invokes mmap("libc")/VirtualAlloc("kernel32"), which does not
        // resolve on every player platform (iOS links libc as __Internal, WebGL has no mmap). Marshal.AllocHGlobal
        // works everywhere; the arena only loses its guard pages.
        => new ArenaAllocator((System.UIntPtr)65536, (System.UIntPtr)(64 * 1024 * 1024), NativeAllocatorBackend.DotNetUnmanaged);
#endif
}
