namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>Tier a site injector applies to.</summary>
public enum SiteTier : byte
{
    /// <summary>Runs only on region maps.</summary>
    Region = 0,
    /// <summary>Runs only on local maps.</summary>
    Local = 1,
    /// <summary>Runs on both region and local maps. Valid for an injector, never for a context.</summary>
    Both = 2,
}
