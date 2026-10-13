namespace RoguelikeToolkit.World.Core.Localization;

/// <summary>Thrown when locale tables fail validation (missing keys, bad YAML).</summary>
public sealed class LocaleDataException : Exception
{
    /// <summary>Creates the exception with the validation or load failure described by <paramref name="message"/>.</summary>
    public LocaleDataException(string message) : base(message) { }
}
