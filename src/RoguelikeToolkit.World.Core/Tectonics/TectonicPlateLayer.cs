using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Tectonics;

public class TectonicPlateLayer : StoreLayer<TectonicPlate>
{
    /// <summary>Plate count the host intends to generate with (read by hosts such as the visualizer).</summary>
    public int SeedCount { get; set; }

    public int Seed { get; }

    /// <summary>Rows reserved in the plate table even for small plate counts.</summary>
    public const int MinPlateCapacity = 256;

    /// <param name="store">Store the layer lives in.</param>
    /// <param name="seedCount">Plate count the host intends to generate with (<see cref="SeedCount"/>).</param>
    /// <param name="seed">Generation seed (<see cref="Seed"/>).</param>
    public TectonicPlateLayer(WorldDataStore store, int seedCount, int seed = 42)
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
