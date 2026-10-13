using RoguelikeToolkit.World.Core.Hydrology;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Managed catalog of connected water bodies (lakes, seas, ocean).</summary>
public sealed class WaterBodyCatalog
{
    /// <summary>All water bodies. As built by the hydrology stage they are ordered by tile count, largest first (ties by id).</summary>
    public List<WaterBody> Bodies { get; } = new();

    /// <summary>Returns the body whose <see cref="WaterBody.Tiles"/> contain <paramref name="tileIndex"/>, or <see langword="null"/> for a tile in no body.</summary>
    /// <remarks>A linear scan over every body's tile list, so use it for single lookups only. <paramref name="fields"/> is not used.</remarks>
    public WaterBody? FindByTile(int tileIndex, HydrologyInfo[]? fields = null)
    {
        foreach (var b in Bodies)
        {
            if (b.Tiles.Contains(tileIndex)) return b;
        }
        return null;
    }
}
