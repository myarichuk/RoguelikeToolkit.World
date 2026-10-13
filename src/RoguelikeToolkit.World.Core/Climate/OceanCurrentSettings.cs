namespace RoguelikeToolkit.World.Core.Climate;

/// <summary>Tunables of the current solver; the defaults are the calibrated ones.</summary>
internal readonly struct OceanCurrentSettings
{
    /// <summary>
    /// Screening length of the stream function, in radians of arc: friction confines each gyre's
    /// influence to about this far. 0.4 rad is ~2500 km, the width of a basin-scale boundary current system.
    /// </summary>
    public double ScreeningLength { get; init; }

    /// <summary>
    /// Width of the western boundary layer at the equator, in radians of arc (it widens with latitude, as beta
    /// falls with cos latitude): the Stommel scale r / beta, a property of the planet and not of the mesh, so the
    /// same world has the same current at every icosphere size. Below about two tile spacings the layer is
    /// not resolved and the scheme saturates at one tile (the monotone limit), so coarse worlds see a wider, weaker jet.
    /// </summary>
    public double BoundaryWidth { get; init; }

    /// <summary>
    /// E-folding distance, in radians of arc, over which a current keeps the temperature it carried
    /// before the surrounding sea drags it back to its local equilibrium, at the reference speed.
    /// </summary>
    public double Reach { get; init; }

    /// <summary>
    /// When > 0, skip the multilevel solve and run this many flat Gauss-Seidel sweeps on the finest mesh: the slow,
    /// obviously-correct reference the hierarchical solver is tested against.
    /// </summary>
    public int ReferenceSweeps { get; init; }

    public static OceanCurrentSettings Default => new OceanCurrentSettings
    {
        ScreeningLength = 0.4,
        BoundaryWidth = 0.06,
        Reach = 0.45,
    };
}
