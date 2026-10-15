namespace RoguelikeToolkit.World.Core.History;

/// <summary>Which settlements a <see cref="Shock"/> reaches.</summary>
public enum ShockFootprint
{
    /// <summary>Only the settlement standing on <see cref="Shock.Tile"/>.</summary>
    Tile = 0,
    /// <summary>Settlements within <see cref="Shock.RadiusDegrees"/> of <see cref="Shock.Tile"/>; severity falls off linearly to zero at the rim.</summary>
    Radius = 1,
    /// <summary>Every standing settlement of <see cref="Shock.PolityId"/> (famine, a polity-wide curse).</summary>
    Polity = 2,
    /// <summary>Every standing settlement.</summary>
    Global = 3,
}
