namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// One exploitable resource: either a mineral deposit (<see cref="Deposit"/> names a <c>DepositType</c>), a
/// land yield (<see cref="Yield"/> names a capacity channel), or neither (a surface work such as a quarry). Carries
/// the era a people needs to work it, a base value, and for deposits an optional lifetime. Data only; loaded and
/// validated by <see cref="PeoplesStore"/> from <c>Peoples/Data/Resources/</c>. M5.1: nothing reads it yet.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class ResourceDef
{
    /// <summary>Resource key, matching the file name (case-insensitive); e.g. <c>iron</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-readable label for pickers. Blank falls back to a title-cased Key.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// A <c>Lithosphere.DepositType</c> name (case-insensitive), or blank when the resource is not a deposit. At most one
    /// resource per deposit name. Peoples sits below Lithosphere, so the name is bound and checked by
    /// <c>History.ResourceDeposits</c>, not here.
    /// </summary>
    public string Deposit { get; set; } = string.Empty;
    /// <summary>A capacity channel: <c>arable</c>, <c>pasture</c>, <c>game</c>, <c>timber</c> or <c>fish</c>; blank when not a land yield. Exclusive with <see cref="Deposit"/>.</summary>
    public string Yield { get; set; } = string.Empty;
    /// <summary>Era needed to work it: <c>ancient</c>, <c>medieval</c> or <c>industrial</c> (validated at load, case-insensitive).</summary>
    public string Era { get; set; } = "Ancient";
    /// <summary>Base value of a work on it, 0-100.</summary>
    public double Value { get; set; } = 1;
    /// <summary>Turns a work on an average deposit lasts before it is spent (scaled by deposit size); 0 never runs out. Deposits only.</summary>
    public int LifetimeTurns { get; set; }

    /// <summary><see cref="Era"/> parsed; only meaningful on a loaded (validated) definition.</summary>
    public ResourceEra EraLevel => TryParseEra(Era, out var e) ? e : ResourceEra.Industrial;

    internal static bool TryParseEra(string? value, out ResourceEra era)
    {
        era = ResourceEra.Ancient;
        return !string.IsNullOrWhiteSpace(value) && !int.TryParse(value, out _)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out era) && era >= ResourceEra.Ancient && era <= ResourceEra.Industrial;
    }
}
