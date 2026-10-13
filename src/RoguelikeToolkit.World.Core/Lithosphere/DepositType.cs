using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Prospectable resources seeded from tectonic, hydrologic and climate setting.</summary>
public enum DepositType : byte
{
    Iron = 0,
    Copper = 1,
    Gold = 2,
    Silver = 3,
    Tin = 4,
    LeadZinc = 5,
    Uranium = 6,
    Coal = 7,
    Oil = 8,
    Gas = 9,
    Salt = 10,
    Gems = 11,
    Bauxite = 12,
    /// <summary>Calcium sulfate evaporite: precipitates first, on the margin of a drying basin, before halite.</summary>
    Gypsum = 13
}
