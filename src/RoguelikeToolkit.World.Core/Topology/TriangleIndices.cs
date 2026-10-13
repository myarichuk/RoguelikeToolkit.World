namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>One triangle of an icosphere mesh, as three indices into the vertex array produced alongside it by <see cref="IcosphereGenerator.Generate"/>.</summary>
public struct TriangleIndices
{
    /// <summary>Vertex indices of the triangle's corners.</summary>
    public int v1, v2, v3;
    /// <summary>Creates a triangle from three vertex indices.</summary>
    public TriangleIndices(int v1, int v2, int v3) { this.v1 = v1; this.v2 = v2; this.v3 = v3; }
}
