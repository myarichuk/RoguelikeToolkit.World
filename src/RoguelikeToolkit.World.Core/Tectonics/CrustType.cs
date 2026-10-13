namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>Kind of crust under a tile; decides subduction polarity at convergent boundaries and lowers the height of oceanic crust.</summary>
public enum CrustType : byte
{
    /// <summary>Light, thick crust that rides high; the overriding side at a convergent boundary with oceanic crust.</summary>
    Continental = 0,
    /// <summary>Dense, thin crust that sits low and dives below continental crust, forming a trench.</summary>
    Oceanic = 1
}
