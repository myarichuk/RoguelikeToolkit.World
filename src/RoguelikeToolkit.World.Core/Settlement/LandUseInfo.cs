using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// What is actually done with a tile. A *decision* layer: Core never sets it by itself; the allocation
/// stages plugins and games add (see <see cref="LandCapacityStage"/>) decide it, and a hand-painted pin
/// (<see cref="UserEditFlags.PinLandUse"/>) wins over all of them. It is not a physics input: no climate,
/// erosion or hydrology stage reads it, so land use can never move a river or a biome.
/// </summary>
public struct LandUseInfo
{
    /// <summary>One of <see cref="LandUseId"/>, or a plugin id from 128 up.</summary>
    public byte Use;
    /// <summary>Bit set of <see cref="LandActivity"/> (plus plugin bits).</summary>
    public byte Activities;
    /// <summary>How hard the ground is worked, 0 (barely) to 255 (to its limit). Plugins may use it as a yield or depletion factor.</summary>
    public byte Intensity;
}
