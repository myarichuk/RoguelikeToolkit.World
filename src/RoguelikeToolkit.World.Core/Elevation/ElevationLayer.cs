using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Elevation;

public class ElevationLayer : StoreLayer<ElevationInfo>
{
    public ElevationLayer(WorldDataStore store) : base(store) { }
}
