using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Instance-level stage contract for stages whose order and layer
/// dependencies are known only at runtime (e.g. Jint script stages).
/// Takes precedence over <see cref="WorldGeneratorStageAttribute"/> when
/// implemented. Attribute-only C# stages are unaffected.
/// </summary>
public interface IDeclaredStage
{
    int Order { get; }
    Type[] Reads { get; }
    Type[] Writes { get; }
    Type[] ReadsOptional { get; }
}
