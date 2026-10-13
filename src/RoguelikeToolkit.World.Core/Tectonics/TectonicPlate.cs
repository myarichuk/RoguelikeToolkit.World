namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>
/// Per-tile tectonic state, 16 bytes. Plate-wide values (drift, base elevation) live
/// in the <see cref="PlateInfo"/> table, reached via <see cref="Id"/>.
/// </summary>
public struct TectonicPlate
{
    // Per-tile continentality (plates carry both continents and oceans, like
    // Earth) and the signed orogeny driver (positive = uplift belts, negative =
    // trenches/rifts) already decayed by distance so elevation and deposits read
    // it directly.
    /// <summary>Signed land-versus-sea tendency of the tile, recentred so the world mean is -0.10; <see cref="Elevation.ElevationGenerationStage"/> takes 0.85 of it as base height. Values above the stage's crust threshold are <see cref="CrustType.Continental"/>.</summary>
    public float Continentality;
    /// <summary>Signed orogeny driver: positive builds fold belts and arcs, negative cuts trenches and rifts. Decays with distance from the boundary and is zero beyond <see cref="TectonicPlateGenerationStage.MaxBoundaryDistance"/> rings.</summary>
    public float Orogeny;
    /// <summary>1-based plate number (row <c>Id - 1</c> of the plate table); 0 = unassigned.</summary>
    public ushort Id;
    // Crust kind (oceanic subducts under continental) and the per-tile boundary
    // classification derived from neighbor drift.
    /// <summary>Crust kind under this tile, from <see cref="Continentality"/>.</summary>
    public CrustType Crust;
    /// <summary>Boundary type at this tile itself (it has a neighbour in another plate); <see cref="PlateBoundaryType.None"/> in plate interiors.</summary>
    public PlateBoundaryType Boundary;
    /// <summary>Type of the nearest boundary tile within range, whose driver this tile's <see cref="Orogeny"/> inherits; <see cref="PlateBoundaryType.None"/> beyond range.</summary>
    public PlateBoundaryType NearestBoundary;
    // Distance to the nearest plate boundary in neighbor rings; 255 = out of range.
    /// <summary>Neighbour-ring distance to the nearest boundary tile (0 on it); 255 means out of range. See <see cref="BoundaryDistance"/>.</summary>
    public byte BoundaryRings;

    /// <summary>Rings to the nearest boundary, or <see cref="float.MaxValue"/> when beyond the BFS cap.</summary>
    public readonly float BoundaryDistance => BoundaryRings == byte.MaxValue ? float.MaxValue : BoundaryRings;

    /// <summary>Highest plate count a tile can address.</summary>
    public const int MaxPlates = ushort.MaxValue - 1;
}
