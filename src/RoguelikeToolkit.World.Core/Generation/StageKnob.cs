namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>
/// Argument checks for the pipeline stages' tuning properties, applied in the setters so a bad value fails where it
/// is assigned (like <see cref="PlanetParameters"/>) instead of surfacing as NaN terrain or a silently skipped pass.
/// </summary>
internal static class StageKnob
{
    /// <summary>Rejects NaN and infinities: one non-finite rate propagates through every downstream tile with no error.</summary>
    public static double Finite(double value, string name)
        => double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite number.")
            : value;

    /// <inheritdoc cref="Finite(double, string)"/>
    public static float Finite(float value, string name)
        => float.IsNaN(value) || float.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite number.")
            : value;

    /// <summary>Counts, iterations and ring widths: zero is a valid "off", a negative number is a mistake.</summary>
    public static int NonNegative(int value, string name)
        => value < 0
            ? throw new ArgumentOutOfRangeException(name, value, $"{name} cannot be negative (use 0 to disable).")
            : value;
}
