namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>Managed catalog of mineral deposits (overlay, not a dense layer).</summary>
public sealed class DepositCatalog
{
    /// <summary>All deposits. As built by <see cref="DepositCatalogBuilder"/> they are grouped by <see cref="DepositType"/> in enum order and ascend by tile index within a type.</summary>
    public List<Deposit> Deposits { get; } = new();

    /// <summary>Returns a new list of the deposits on <paramref name="tileIndex"/> (empty when none); a linear scan of the whole catalog per call.</summary>
    public List<Deposit> AtTile(int tileIndex)
    {
        var found = new List<Deposit>();
        foreach (var d in Deposits)
        {
            if (d.TileIndex == tileIndex) found.Add(d);
        }
        return found;
    }
}
