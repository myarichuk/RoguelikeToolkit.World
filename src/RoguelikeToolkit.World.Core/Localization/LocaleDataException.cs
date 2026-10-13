namespace RoguelikeToolkit.World.Core.Localization;

/// <summary>Thrown when locale tables fail validation (missing keys, bad YAML).</summary>
public sealed class LocaleDataException : Exception
{
    public LocaleDataException(string message) : base(message) { }
}
