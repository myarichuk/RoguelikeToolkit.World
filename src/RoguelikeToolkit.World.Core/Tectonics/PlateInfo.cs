using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>
/// Per-plate parameters, one row per plate in a store table (see
/// <see cref="WorldDataStore.RegisterTable{T}"/>). Tiles refer to a row through
/// <see cref="TectonicPlate.Id"/>, so this data is stored once per plate instead of
/// once per tile.
/// </summary>
public struct PlateInfo
{
    /// <summary>The plate's random base elevation draw, 0..1; it offsets the continentality of the plate's tiles.</summary>
    public double Elevation;
    /// <summary>Relative drift speed, 0..1; multiplies the drift direction when computing convergence between neighbouring plates.</summary>
    public double DriftSpeed;
    // Unit drift direction (used for boundary convergence). Kept as components
    // to keep the struct blittable for the data store.
    /// <summary>X component of the unit drift direction.</summary>
    public double DriftX;
    /// <summary>Y component of the unit drift direction.</summary>
    public double DriftY;
    /// <summary>Z component of the unit drift direction.</summary>
    public double DriftZ;
}
