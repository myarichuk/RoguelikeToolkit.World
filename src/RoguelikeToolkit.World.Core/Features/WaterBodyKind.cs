using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core;

/// <summary>Water body kinds for game queries (lake vs sea vs ocean basin).</summary>
public enum WaterBodyKind : byte
{
    Lake = 0,
    Sea = 1,
    Ocean = 2
}
