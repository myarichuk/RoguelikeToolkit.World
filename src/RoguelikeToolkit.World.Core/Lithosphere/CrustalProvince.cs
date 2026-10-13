using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// The tectonic setting a tile sits in, as an ore geologist would name it. Metals
/// and fuels are not scattered by climate; they follow the crust that hosts them,
/// so <see cref="DepositCatalogBuilder"/> keys its rules on this instead of raw
/// boundary flags.
/// </summary>
public enum CrustalProvince : byte
{
    /// <summary>Oceanic crust away from any boundary: abyssal plain.</summary>
    OceanBasin = 0,
    /// <summary>Oceanic crust at a divergent boundary: mid-ocean ridge.</summary>
    SpreadingRidge = 1,
    /// <summary>Oceanic crust at a convergent boundary: trench and island arc.</summary>
    Subduction = 2,
    /// <summary>Ancient, stable continental interior (Canadian, Baltic, Yilgarn shields): old basement far from any active margin.</summary>
    Craton = 3,
    /// <summary>Continental crust shortened and uplifted at a convergent boundary: fold belt, magmatic arc.</summary>
    Orogen = 4,
    /// <summary>Continental crust pulled apart at a divergent boundary: rift valley, failed arm.</summary>
    Rift = 5,
    /// <summary>Everything else on continental crust: sedimentary platform, passive margin, foreland, strike-slip basin.</summary>
    Platform = 6,
}
