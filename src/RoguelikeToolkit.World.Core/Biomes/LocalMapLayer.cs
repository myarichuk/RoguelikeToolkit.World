using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Biomes;

public class LocalMapLayer : StoreLayer<LocalMapInfo>
{
    /// <param name="store">Store the layer lives in.</param>
    /// <param name="seed">Ignored; biome derivation reads the store directly. Kept for source compatibility.</param>
    /// <param name="tectonicLayer">Ignored; kept for source compatibility.</param>
    public LocalMapLayer(WorldDataStore store, int seed, TectonicPlateLayer tectonicLayer) : base(store) { }
}
