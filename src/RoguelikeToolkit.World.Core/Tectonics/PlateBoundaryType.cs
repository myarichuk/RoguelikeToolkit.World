namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>How the plates on either side of a tile meet, judged from their relative drift across the boundary.</summary>
public enum PlateBoundaryType : byte
{
    /// <summary>Not on a boundary (all neighbours belong to the same plate).</summary>
    None = 0,
    /// <summary>Plates move towards each other: fold belts, volcanic arcs and trenches.</summary>
    Convergent = 1,
    /// <summary>Plates move apart: mid-ocean ridges on oceanic crust, rift valleys on continental.</summary>
    Divergent = 2,
    /// <summary>Plates slide past each other without much closing or opening; a weak, narrow uplift.</summary>
    Transform = 3
}
