namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Keeps a type (and its members) from being removed by Unity's managed code stripping (UnityLinker honors any
/// attribute named <c>PreserveAttribute</c>, so Core does not need a UnityEngine reference). Applied to types that
/// are only ever reached through reflection, e.g. stages created by <see cref="Generation.WorldGenerationPipeline.Discover(string?, Action{Generation.StageDiscoveryDiagnostic}?)"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
                AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
                AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
internal sealed class PreserveAttribute : Attribute
{
}
