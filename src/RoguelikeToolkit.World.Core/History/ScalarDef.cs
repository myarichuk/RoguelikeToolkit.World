namespace RoguelikeToolkit.World.Core.History;

/// <summary>A declared per-polity scalar: interned name, clamp range and the value a new polity starts with.</summary>
public sealed record ScalarDef(int Id, string Name, double Min, double Max, double Default)
{
    /// <summary>Clamps <paramref name="value"/> to <see cref="Min"/>..<see cref="Max"/>.</summary>
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}
