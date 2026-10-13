namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Sparse feature: a labeled tile segment (ranges, valleys, canyons, playas).</summary>
public enum RangeKind : byte
{
    /// <summary>Connected high ground: tiles whose height is at or above the builder's mountain threshold (0.5 by default).</summary>
    MountainRange = 0,
    /// <summary>River-side lowland: non-river land tiles no higher than 0.35 that touch a river tile. Only built when hydrology is present.</summary>
    Valley = 1,
    /// <summary>Arid slot-gorge reach: a powerful river cutting relief.</summary>
    Canyon = 2,
    /// <summary>Dry basin floor: salt flat / playa.</summary>
    Playa = 3
}
