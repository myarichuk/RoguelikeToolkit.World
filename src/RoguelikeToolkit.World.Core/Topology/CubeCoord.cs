namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>Cube coordinates for a hex (x + y + z == 0); axial q = x, r = z.</summary>
public readonly record struct CubeCoord(int X, int Y, int Z)
{
    public bool IsValid => X + Y + Z == 0;

    public (int Q, int R) ToAxial() => (X, Z);

    public static CubeCoord FromAxial(int q, int r) => new(q, -q - r, r);
}
