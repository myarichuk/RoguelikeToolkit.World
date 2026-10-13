using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Managed catalog of mineral deposits (overlay, not a dense layer).</summary>
public sealed class DepositCatalog
{
    public List<Deposit> Deposits { get; } = new();

    public List<Deposit> AtTile(int tileIndex)
    {
        var found = new List<Deposit>();
        foreach (var d in Deposits)
        {
            if (d.TileIndex == tileIndex) found.Add(d);
        }
        return found;
    }
}
