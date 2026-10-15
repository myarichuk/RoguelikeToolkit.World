namespace RoguelikeToolkit.World.Core.History;

/// <summary>Why a settlement exists; decides how shocks and wars treat it.</summary>
public enum SettlementRole
{
    /// <summary>Seat of a polity.</summary>
    Capital = 0,
    /// <summary>Founded by population overflow.</summary>
    Colony = 1,
    /// <summary>Economic node on a route or resource.</summary>
    Node = 2,
    /// <summary>Military post.</summary>
    Post = 3,
    /// <summary>Religious centre.</summary>
    Shrine = 4,
    /// <summary>Refuge founded by displaced population.</summary>
    Refuge = 5,
}
