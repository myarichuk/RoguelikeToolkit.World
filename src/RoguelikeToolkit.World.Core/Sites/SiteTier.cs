using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Tier a site injector applies to.</summary>
public enum SiteTier : byte
{
    Region = 0,
    Local = 1,
    Both = 2,
}
