using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Sparse feature: one ore/fuel/mineral occurrence on a tile.</summary>
public sealed class Deposit
{
    public int Id { get; }
    public DepositType Type { get; }
    public int TileIndex { get; }
    public float Richness { get; }
    public DepositSize Size { get; }

    public Deposit(int id, DepositType type, int tileIndex, float richness)
    {
        Id = id;
        Type = type;
        TileIndex = tileIndex;
        Richness = richness;
        Size = richness >= 0.85f ? DepositSize.Rich
            : richness >= 0.65f ? DepositSize.Large
            : richness >= 0.45f ? DepositSize.Medium
            : DepositSize.Small;
    }
}
