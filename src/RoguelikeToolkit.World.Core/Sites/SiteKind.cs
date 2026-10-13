using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Kind of a placed site.</summary>
public enum SiteKind : byte
{
    Ruin = 0,
    City = 1,
    Mine = 2,
    Landmark = 3,
}
