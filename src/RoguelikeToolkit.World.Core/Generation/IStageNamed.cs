namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>Optional friendly name for runtime stages (logs/diagnostics).</summary>
public interface IStageNamed
{
    string Name { get; }
}
