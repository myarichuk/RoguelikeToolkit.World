using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public struct TriangleIndices
{
    public int v1, v2, v3;
    public TriangleIndices(int v1, int v2, int v3) { this.v1 = v1; this.v2 = v2; this.v3 = v3; }
}
