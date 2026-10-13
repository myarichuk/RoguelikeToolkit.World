using System;

namespace RoguelikeToolkit.World.Core;

public class ClimateLayer : StoreLayer<ClimateInfo>
{
    public ClimateLayer(WorldDataStore store) : base(store) { }
}
