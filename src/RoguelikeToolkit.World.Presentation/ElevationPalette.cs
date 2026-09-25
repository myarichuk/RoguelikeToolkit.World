using System.Numerics;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Height ramp for the visualizer's elevation color mode.
/// Deep ocean → shallow → lowland → highland → peak.
/// </summary>
public static class ElevationPalette
{
    public static Vector3 ColorFor(float height)
    {
        // Piecewise-linear ramp over normalized t = (h + 1) / 2.
        float t = (height + 1f) * 0.5f;
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;

        if (t < 0.35f)
            return Lerp(new Vector3(0.05f, 0.15f, 0.40f), new Vector3(0.20f, 0.50f, 0.80f), t / 0.35f);
        if (t < 0.55f)
            return Lerp(new Vector3(0.20f, 0.50f, 0.80f), new Vector3(0.30f, 0.60f, 0.30f), (t - 0.35f) / 0.20f);
        if (t < 0.8f)
            return Lerp(new Vector3(0.30f, 0.60f, 0.30f), new Vector3(0.55f, 0.45f, 0.30f), (t - 0.55f) / 0.25f);
        return Lerp(new Vector3(0.55f, 0.45f, 0.30f), new Vector3(0.90f, 0.90f, 0.90f), (t - 0.8f) / 0.2f);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}
