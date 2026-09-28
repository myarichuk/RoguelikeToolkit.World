using System.Numerics;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// CPU-side face normals for the visualizer's Terrain relief view (backlog
/// D1). Normals are computed once per mesh rebuild from the displaced
/// positions and uploaded to <c>aNormal</c> through the existing static-VBO
/// path, so this stays GLES-1.00-safe (no shader-language upgrade).
/// </summary>
public static class TerrainNormals
{
    /// <summary>
    /// Unit face normal of triangle (a, b, c) via (b−a)×(c−a). Returns
    /// <paramref name="fallback"/> for degenerate input; never NaN.
    /// </summary>
    public static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c, Vector3 fallback)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        if (len < 1e-12f || float.IsNaN(len) || float.IsInfinity(len))
            return fallback;
        return n / len;
    }

    /// <summary>
    /// Orients a face normal outward: flips it when it points against the
    /// face centroid, which makes the result robust to mesh winding.
    /// </summary>
    public static Vector3 Outward(Vector3 normal, Vector3 centroid)
        => Vector3.Dot(normal, centroid) < 0f ? -normal : normal;
}
