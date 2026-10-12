using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>What a tile's <see cref="UserEditInfo"/> overrides.</summary>
[Flags]
public enum UserEditFlags : byte
{
    None = 0,
    /// <summary>The surface height is fixed to <see cref="UserEditInfo.PinHeight"/> after erosion ("fix", not "sculpt").</summary>
    PinHeight = 1,
    /// <summary>The biome is fixed to <see cref="UserEditInfo.PinBiome"/>: hydrology classifies its cover from it and the biome stage keeps it.</summary>
    PinBiome = 2,
}

/// <summary>
/// A hand-made change to one tile, as a layer the pipeline treats as an *input*: no stage writes it,
/// so a re-run never clears it. Together with the seed it fully determines the world, which is what
/// makes editing re-derivable (<see cref="World.Recompute"/>) and keeps a saved edit small: the
/// document stores these sparse deltas, never the derived fields.
/// <para>
/// Two kinds of height edit, because they mean different things to erosion. <see cref="HeightDelta"/>
/// is a *sculpt*: it is added to the tectonic relief before climate and erosion run, so a raised ridge
/// casts a rain shadow, sheds talus and drains like any mountain. <see cref="PinHeight"/> is a *fix*:
/// the final surface is forced to that value after erosion, for a mesa or a lake basin that must stay
/// exactly where it was drawn.
/// </para>
/// </summary>
public struct UserEditInfo
{
    /// <summary>Added to the base relief before climate and erosion (sculpt).</summary>
    public float HeightDelta;
    /// <summary>Final height where <see cref="UserEditFlags.PinHeight"/> is set (fix).</summary>
    public float PinHeight;
    public BiomeType PinBiome;
    public UserEditFlags Flags;

    public readonly bool IsEmpty => HeightDelta == 0f && Flags == UserEditFlags.None;
}

public class UserEditLayer : StoreLayer<UserEditInfo>
{
    public UserEditLayer(WorldDataStore store) : base(store) { }
}
