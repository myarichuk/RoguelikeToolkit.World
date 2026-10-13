using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Hydrology;

public class HydrologyLayer : StoreLayer<HydrologyInfo>
{
    public HydrologyLayer(WorldDataStore store) : base(store) { }
}
