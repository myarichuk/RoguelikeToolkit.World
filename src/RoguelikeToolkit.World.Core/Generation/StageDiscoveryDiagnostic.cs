namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>A non-fatal failure found while discovering or loading stages (a plugin assembly that would not load, a stage that would not construct).</summary>
public sealed class StageDiscoveryDiagnostic
{
    /// <summary>What failed, including the assembly or stage type name where known.</summary>
    public string Message { get; }
    /// <summary>The underlying exception.</summary>
    public Exception Exception { get; }

    /// <summary>Creates a diagnostic.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="exception">The underlying exception.</param>
    public StageDiscoveryDiagnostic(string message, Exception exception)
    {
        Message = message;
        Exception = exception;
    }
}
