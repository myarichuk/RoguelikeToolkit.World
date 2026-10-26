namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>Settings for <see cref="TemplateChronicleTextProvider"/>.</summary>
public sealed class ChronicleOptions
{
    private int _turnYears = 10;

    /// <summary>
    /// Years per history turn, as the history was run with (<c>HistoryContext.TurnYears</c>, default 10); dates in the text are
    /// <see cref="FirstYear"/> + turn × this + the year within the turn. Must be positive.
    /// </summary>
    public int TurnYears
    {
        get => _turnYears;
        set => _turnYears = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TurnYears must be positive.");
    }

    /// <summary>The year turn 0 begins in (default 1).</summary>
    public int FirstYear { get; set; } = 1;

    /// <summary>The host's names; null draws them from the shipped name tables (see <see cref="IChronicleNameResolver"/>).</summary>
    public IChronicleNameResolver? Names { get; set; }

    /// <summary>
    /// A directory with <c>Grammar/*.yaml</c> and <c>Lexicon/*.yaml</c> files that replace the shipped files of the same name or
    /// add overlays; null uses the shipped data only.
    /// </summary>
    public string? DataDirectory { get; set; }
}
