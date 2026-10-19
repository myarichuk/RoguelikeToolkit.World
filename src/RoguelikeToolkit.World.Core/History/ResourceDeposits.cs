using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Binds a <see cref="ResourceDef.Deposit"/> name to the <see cref="DepositType"/> the lithosphere seeds. The peoples
/// tables sit below Lithosphere in the layering, so they keep the deposit as a name and the binding lives here, with
/// the history that reads both. M5.1: nothing in the sim calls it yet.
/// </summary>
public static class ResourceDeposits
{
    /// <summary>The deposit <paramref name="resource"/> works; false for a surface work or an unknown name.</summary>
    public static bool TryGetDeposit(ResourceDef resource, out DepositType deposit)
    {
        ArgumentNullException.ThrowIfNull(resource);
        deposit = default;
        string name = resource.Deposit?.Trim() ?? string.Empty;
        return name.Length > 0 && !char.IsDigit(name[0]) && name[0] != '-'
            && Enum.TryParse(name, ignoreCase: true, out deposit) && deposit <= DepositType.Gypsum;
    }

    /// <summary>One line per resource whose <see cref="ResourceDef.Deposit"/> names no <see cref="DepositType"/>; empty when all bind.</summary>
    public static IReadOnlyList<string> Problems(PeoplesStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var problems = new List<string>();
        foreach (var r in store.Resources.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            if (!string.IsNullOrWhiteSpace(r.Deposit) && !TryGetDeposit(r, out _))
                problems.Add($"Resources/{r.Key}.yaml: unknown Deposit '{r.Deposit}' (want a DepositType name such as Iron, or blank).");
        return problems;
    }
}
