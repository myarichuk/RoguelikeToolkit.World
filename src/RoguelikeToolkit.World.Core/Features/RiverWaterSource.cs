using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Where a river tile's water comes from (head classification).</summary>
public enum RiverWaterSource : byte
{
    None = 0,
    /// <summary>Rain/groundwater-fed head: no glacier upstream.</summary>
    Spring = 1,
    /// <summary>The river's head sits on glaciated ground (meltwater-fed).</summary>
    GlacierMelt = 2
}
