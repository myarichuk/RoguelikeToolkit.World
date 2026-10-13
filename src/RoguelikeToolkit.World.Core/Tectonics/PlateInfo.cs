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
    public double Elevation;
    public double DriftSpeed;
    // Unit drift direction (used for boundary convergence). Kept as components
    // to keep the struct blittable for the data store.
    public double DriftX;
    public double DriftY;
    public double DriftZ;
}
