using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Climate;

/// <summary>The solved surface circulation: stream function, velocity and the heat the currents carry.</summary>
internal sealed class OceanCurrentField
{
    /// <summary>Stream function per tile (0 on land and so on every coast: no flow through a shore).</summary>
    public double[] Psi = Array.Empty<double>();

    /// <summary>
    /// Surface current per tile, tangent to the sphere, in units of <see cref="ReferenceSpeed"/>
    /// (1 = a strong boundary current; zero on land).
    /// </summary>
    public Vector3D[] Velocity = Array.Empty<Vector3D>();

    /// <summary>
    /// Sea-surface temperature minus the latitude's radiative equilibrium, in climate temperature units, per
    /// ocean tile: positive where a current carries tropical water poleward, negative where it brings polar
    /// water to the equator. Zero on land.
    /// </summary>
    public double[] Anomaly = Array.Empty<double>();

    /// <summary>The speed (stream-function units per radian) that <see cref="Velocity"/> is normalised by.</summary>
    public double ReferenceSpeed;
}
