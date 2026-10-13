using System;

namespace RoguelikeToolkit.World.Core;

public class HydrologyLayer : StoreLayer<HydrologyInfo>
{
    public HydrologyLayer(WorldDataStore store) : base(store) { }
}
