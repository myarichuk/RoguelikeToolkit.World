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
    public float Continentality;
    public float Orogeny;
    /// <summary>1-based plate number (row <c>Id - 1</c> of the plate table); 0 = unassigned.</summary>
    public ushort Id;
    // Crust kind (oceanic subducts under continental) and the per-tile boundary
    // classification derived from neighbor drift.
    public CrustType Crust;
    public PlateBoundaryType Boundary;
    public PlateBoundaryType NearestBoundary;
    // Distance to the nearest plate boundary in neighbor rings; 255 = out of range.
    public byte BoundaryRings;

    /// <summary>Rings to the nearest boundary, or <see cref="float.MaxValue"/> when beyond the BFS cap.</summary>
    public readonly float BoundaryDistance => BoundaryRings == byte.MaxValue ? float.MaxValue : BoundaryRings;

    /// <summary>Highest plate count a tile can address.</summary>
    public const int MaxPlates = ushort.MaxValue - 1;
}
