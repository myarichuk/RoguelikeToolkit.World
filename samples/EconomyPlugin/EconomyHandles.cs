using RoguelikeToolkit.World.Core.History;

namespace EconomyPlugin;

/// <summary>Resolved scalar ids for one run; null when the host did not declare the economy scalars.</summary>
internal sealed record EconomyHandles(int Wealth, int Tax, int Corruption, int Unrest, int Estates)
{
    public static EconomyHandles? Resolve(ScalarRegistry scalars)
    {
        if (!scalars.TryGet(EconomyScalars.Wealth, out var w) || !scalars.TryGet(EconomyScalars.Tax, out var t)
            || !scalars.TryGet(EconomyScalars.Corruption, out var c) || !scalars.TryGet(EconomyScalars.Unrest, out var u)
            || !scalars.TryGet(EconomyScalars.Estates, out var e)) return null;
        return new EconomyHandles(w.Id, t.Id, c.Id, u.Id, e.Id);
    }
}
