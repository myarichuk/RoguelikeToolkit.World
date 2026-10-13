using System;
using System.Linq;

namespace RoguelikeToolkit.World.Core;

/// <summary>Optional friendly name for runtime stages (logs/diagnostics).</summary>
public interface IStageNamed
{
    string Name { get; }
}
