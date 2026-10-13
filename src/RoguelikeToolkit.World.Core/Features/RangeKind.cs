namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Sparse feature: a labeled tile segment (ranges, valleys, canyons, playas).</summary>
public enum RangeKind : byte
{
    MountainRange = 0,
    Valley = 1,
    /// <summary>Arid slot-gorge reach: a powerful river cutting relief.</summary>
    Canyon = 2,
    /// <summary>Dry basin floor: salt flat / playa.</summary>
    Playa = 3
}
