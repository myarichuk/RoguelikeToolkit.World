namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>Cube coordinates for a hex (x + y + z == 0); axial q = x, r = z.</summary>
public readonly record struct CubeCoord(int X, int Y, int Z)
{
    /// <summary>True when the coordinate satisfies <c>X + Y + Z == 0</c>; the constructor does not enforce it.</summary>
    public bool IsValid => X + Y + Z == 0;

    /// <summary>The axial pair (Q, R) = (<see cref="CubeCoord.X"/>, <see cref="CubeCoord.Z"/>).</summary>
    public (int Q, int R) ToAxial() => (X, Z);

    /// <summary>The cube coordinate <c>(q, -q - r, r)</c> for an axial pair.</summary>
    public static CubeCoord FromAxial(int q, int r) => new(q, -q - r, r);
}
