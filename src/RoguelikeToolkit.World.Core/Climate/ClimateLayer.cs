using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Climate;

public class ClimateLayer : StoreLayer<ClimateInfo>
{
    public ClimateLayer(WorldDataStore store) : base(store) { }
}
