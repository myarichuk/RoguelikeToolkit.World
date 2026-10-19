namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// How advanced a people must be to exploit a <see cref="ResourceDef"/>. The history caps every resource at one
/// world-wide <see cref="History.HistoryContext.MaxResourceEra"/>; a resource above the cap is never exploited.
/// </summary>
public enum ResourceEra
{
    /// <summary>Stone, salt, timber, open-pit copper and gold: worked since before cities.</summary>
    Ancient = 0,
    /// <summary>Deep shafts, smelting and coal: the default fantasy cap.</summary>
    Medieval = 1,
    /// <summary>Oil, gas, uranium, aluminium: out of reach of a fantasy setting unless the cap is raised.</summary>
    Industrial = 2
}
