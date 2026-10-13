using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeToolkit.World.Core;

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
