namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>
/// Instance-level stage contract for stages whose order and layer
/// dependencies are known only at runtime (e.g. Jint script stages).
/// Takes precedence over <see cref="WorldGeneratorStageAttribute"/> when
/// implemented. Attribute-only C# stages are unaffected.
/// </summary>
public interface IDeclaredStage
{
    /// <summary>Execution order; stages run in ascending order and ties keep registration order (see <see cref="StageOrder"/>).</summary>
    int Order { get; }
    /// <summary>Field-layer struct types this stage requires: each must be registered in the store, and the stage may not run before the first writer of any of them.</summary>
    Type[] Reads { get; }
    /// <summary>Field-layer struct types this stage writes. A second writer of the same layer must also list it in <see cref="Reads"/>, or the contract validator rejects the pipeline.</summary>
    Type[] Writes { get; }
    /// <summary>Field-layer types read only if registered; exempt from ordering and registration validation.</summary>
    Type[] ReadsOptional { get; }
}
