namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>Kind of a placed site.</summary>
public enum SiteKind : byte
{
    /// <summary>Remnants of an abandoned settlement or structure.</summary>
    Ruin = 0,
    /// <summary>An inhabited settlement.</summary>
    City = 1,
    /// <summary>A mining site tied to a planet deposit.</summary>
    Mine = 2,
    /// <summary>A prominent natural point of interest, such as the highest ground.</summary>
    Landmark = 3,
}
