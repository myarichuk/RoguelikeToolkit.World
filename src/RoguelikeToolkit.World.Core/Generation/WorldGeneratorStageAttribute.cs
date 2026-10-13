using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>
/// Declares a pipeline stage's execution order plus the field-layer contracts
/// it reads and writes. Reads/Writes are field-layer struct types stored in
/// <see cref="WorldDataStore"/> (e.g. typeof(ElevationInfo)). Sparse feature
/// catalogs (rivers, water bodies) are built outside the store and are not
/// part of this contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WorldGeneratorStageAttribute : Attribute
{
    /// <summary>Execution order; stages run in ascending order (see <see cref="StageOrder"/>). Discovery rejects two attributed stages sharing a value.</summary>
    public int Order { get; }

    /// <summary>Field-layer types this stage reads. Empty means "undeclared, skip validation".</summary>
    public Type[] Reads { get; set; } = Array.Empty<Type>();

    /// <summary>
    /// Field-layer types this stage reads only when registered (graceful
    /// fallback otherwise, e.g. moisture without climate). Optional reads are
    /// skipped by ordering and registration validation, so a stage using them
    /// may run before their writer or without the layer entirely.
    /// </summary>
    public Type[] ReadsOptional { get; set; } = Array.Empty<Type>();

    /// <summary>Field-layer types this stage mutates. Empty means "undeclared, skip validation".</summary>
    public Type[] Writes { get; set; } = Array.Empty<Type>();

    /// <summary>Declares a stage's execution order.</summary>
    /// <param name="order">Execution order; prefer a <see cref="StageOrder"/> constant plus an offset over a bare literal.</param>
    public WorldGeneratorStageAttribute(int order)
    {
        Order = order;
    }
}
