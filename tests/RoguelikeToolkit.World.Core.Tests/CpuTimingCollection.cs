using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Classes in this collection run with no other test class executing beside them.
/// <see cref="LayerTests"/> holds a wall-clock ratio test that flaked (~8% of full-suite
/// runs, on main too) whenever another CPU-heavy class ran in parallel; the DetMath
/// accuracy sweeps would add to that contention, so they join it.
/// </summary>
[CollectionDefinition("CpuTiming", DisableParallelization = true)]
public sealed class CpuTimingCollection { }
