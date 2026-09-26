using System;

namespace RoguelikeToolkit.World.Core;

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

    public WorldGeneratorStageAttribute(int order)
    {
        Order = order;
    }
}
