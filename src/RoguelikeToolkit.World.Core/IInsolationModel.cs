using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Supplies the annual-mean stellar energy reaching a point of the surface, relative to the mean over
/// the whole sphere (1.0 = the planet-wide average). Implement it for worlds whose heating is not the
/// textbook "spinning, tilted ball around one sun": a tidally locked planet, a binary star.
/// Must be a pure function of the position (determinism).
/// </summary>
public interface IInsolationModel
{
    /// <param name="unitPosition">Unit vector from the planet's centre to the surface point (+Z is the spin axis).</param>
    double RelativeInsolation(Vector3D unitPosition);
}
