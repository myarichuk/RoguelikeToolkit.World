namespace RoguelikeToolkit.World.Core.Primitives;

/// <summary>An immutable double-precision 2D vector; a plain value pair with no operations.</summary>
/// <param name="X">Horizontal component.</param>
/// <param name="Y">Vertical component.</param>
public readonly record struct Vector2D(double X, double Y);
