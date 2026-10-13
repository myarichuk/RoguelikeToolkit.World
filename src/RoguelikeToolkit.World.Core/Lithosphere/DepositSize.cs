namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>Deposit richness class, derived from prospectivity score.</summary>
public enum DepositSize : byte
{
    /// <summary>Richness below 0.45: a minor occurrence.</summary>
    Small = 0,
    /// <summary>Richness from 0.45 up to 0.65.</summary>
    Medium = 1,
    /// <summary>Richness from 0.65 up to 0.85.</summary>
    Large = 2,
    /// <summary>Richness of 0.85 or more: the best-hosted ground for the commodity.</summary>
    Rich = 3
}
