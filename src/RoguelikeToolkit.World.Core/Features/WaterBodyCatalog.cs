using RoguelikeToolkit.World.Core.Hydrology;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Managed catalog of connected water bodies (lakes, seas, ocean).</summary>
public sealed class WaterBodyCatalog
{
    public List<WaterBody> Bodies { get; } = new();

    public WaterBody? FindByTile(int tileIndex, HydrologyInfo[]? fields = null)
    {
        foreach (var b in Bodies)
        {
            if (b.Tiles.Contains(tileIndex)) return b;
        }
        return null;
    }
}
