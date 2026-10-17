namespace RoguelikeToolkit.World.Core.History;

/// <summary>Outcome of <see cref="PluginStateBag.TryRead"/>.</summary>
public enum PluginStateLookup
{
    /// <summary>No blob is stored under the key.</summary>
    Missing,

    /// <summary>A blob is stored and its version matches the expected one.</summary>
    Found,

    /// <summary>A blob is stored under a different version; the plugin decides to migrate or <see cref="PluginStateBag.Reset"/>.</summary>
    VersionMismatch,
}
