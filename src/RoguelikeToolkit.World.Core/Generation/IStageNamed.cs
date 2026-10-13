namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>Optional friendly name for runtime stages (logs/diagnostics).</summary>
public interface IStageNamed
{
    /// <summary>Human-readable stage name used in diagnostics and contract error messages.</summary>
    string Name { get; }
}
