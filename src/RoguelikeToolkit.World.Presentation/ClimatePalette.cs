using System.Numerics;

namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Temperature × moisture diagnostic for the visualizer's climate color mode.
/// Red channel carries temperature, green carries precipitation, blue carries
/// cold: deserts read red, tropics yellow-green, polar deserts blue, and the
/// temperate humid belt greens.
/// </summary>
public static class ClimatePalette
{
    public static Vector3 ColorFor(float temperature, float precipitation)
    {
        float t = temperature < 0f ? 0f : (temperature > 1f ? 1f : temperature);
        float p = precipitation < 0f ? 0f : (precipitation > 1f ? 1f : precipitation);
        return new Vector3(t, p * 0.9f + t * 0.1f, (1f - t) * 0.85f + p * 0.15f);
    }
}
