using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Default arena construction. Sizes are modest; generation temporaries are kilobytes.
/// </summary>
public static class ArenaDefaults
{
    public static ArenaAllocator Create()
        => new ArenaAllocator((System.UIntPtr)65536, (System.UIntPtr)(64 * 1024 * 1024));
}
