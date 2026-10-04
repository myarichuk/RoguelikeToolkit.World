using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace RoguelikeToolkit.World.Core;

public enum CrustType : byte
{
    Continental = 0,
    Oceanic = 1
}

public enum PlateBoundaryType : byte
{
    None = 0,
    Convergent = 1,
    Divergent = 2,
    Transform = 3
}

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

public class TectonicPlateLayer : StoreLayer<TectonicPlate>
{
    /// <summary>Plate count the host intends to generate with (read by hosts such as the visualizer).</summary>
    public int SeedCount { get; set; }

    public int Seed { get; }

    /// <summary>Rows reserved in the plate table even for small plate counts.</summary>
    public const int MinPlateCapacity = 256;

    /// <param name="arena">Ignored; kept for source compatibility. The layer allocates nothing: plate scratch lives in <see cref="TectonicPlateGenerationStage"/>.</param>
    public TectonicPlateLayer(WorldDataStore store, int seedCount, int seed = 42, ArenaAllocator? arena = null)
        : base(store)
    {
        if ((uint)seedCount > TectonicPlate.MaxPlates)
            throw new ArgumentOutOfRangeException(nameof(seedCount), seedCount, $"Plate count must be 0..{TectonicPlate.MaxPlates}.");
        SeedCount = seedCount;
        Seed = seed;
        // Plate rows are stored once per plate, not per tile. The floor lets a stage
        // run with a different plate count than the layer was built with (10 KB).
        store.RegisterTable<PlateInfo>(Math.Max(seedCount, MinPlateCapacity));
    }
}
