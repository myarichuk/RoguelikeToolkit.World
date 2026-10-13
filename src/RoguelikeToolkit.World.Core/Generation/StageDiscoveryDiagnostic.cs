namespace RoguelikeToolkit.World.Core.Generation;

public sealed class StageDiscoveryDiagnostic
{
    public string Message { get; }
    public Exception Exception { get; }

    public StageDiscoveryDiagnostic(string message, Exception exception)
    {
        Message = message;
        Exception = exception;
    }
}
