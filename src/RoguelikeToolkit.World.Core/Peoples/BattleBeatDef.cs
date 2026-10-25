namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// A battle beat and when it can happen (see <see cref="BattleDef.Beats"/>). Conditions are tokens, all of which must hold; a
/// leading <c>!</c> negates one: <c>season=Winter</c>, <c>terrain=forest</c>, <c>kind=Ambush</c>, <c>river</c>,
/// <c>initiative=attacker|defender</c>, <c>winner=attacker|defender|none</c>,
/// <c>outcome=decisive|victory|pyrrhic|stalemate</c>, <c>winner_tactic=X</c>, <c>loser_tactic=X</c>, <c>winner_arm=X</c>,
/// <c>loser_arm=X</c>, <c>custom=X</c> (a custom of the winner), <c>general_fell</c>, <c>general_captured</c>, <c>river_bypassed</c>,
/// <c>declined</c> (a side refused the field; only beats asking for it are drawn then).
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class BattleBeatDef
{
    /// <summary>Beat name (interned in the battle record).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Weight in the turning-point draw (&gt; 0).</summary>
    public double Weight { get; set; } = 1.0;
    /// <summary>Conditions, all of which must hold.</summary>
    public List<string> When { get; set; } = new();
    /// <summary>Recorded whenever its conditions hold, after the turning point, instead of competing in the draw.</summary>
    public bool Always { get; set; }
}
