using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Biomes;

/// <summary>Store-backed layer exposing the per-tile <see cref="BiomeInfo"/> field written by <see cref="BiomeGenerationStage"/>.</summary>
public class BiomeLayer : StoreLayer<BiomeInfo>
{
    /// <param name="store">Store the layer lives in.</param>
    /// <param name="seed">Ignored; biome derivation reads the store directly. Kept for source compatibility.</param>
    /// <param name="tectonicLayer">Ignored; kept for source compatibility.</param>
    public BiomeLayer(WorldDataStore store, int seed, TectonicPlateLayer tectonicLayer) : base(store) { }
}
