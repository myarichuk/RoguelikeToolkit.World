namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// How sieges run (<c>Peoples/Data/Siege/default.yaml</c>; the history reads <c>default</c>): a month loop of supply, disease,
/// storms, hunger, surrender, betrayal and relief, up to the history's <c>MaxSiegeMonths</c>. Shares are 0..1.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class SiegeDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Share of a settlement's people who hold its walls, per tier name.</summary>
    public Dictionary<string, double> GarrisonShare { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Morale of the defenders behind their walls.</summary>
    public double DefenderMorale { get; set; } = 0.8;

    // Food.
    /// <summary>Months of food in any besieged place.</summary>
    public double FoodBase { get; set; } = 6;
    /// <summary>Further months in a city or larger.</summary>
    public double FoodCity { get; set; } = 6;
    /// <summary>Further months while a river or the sea still brings food in (not blockaded).</summary>
    public double FoodResupply { get; set; } = 4;
    /// <summary>Months added or lost by the season the siege begins in (stores are full after harvest).</summary>
    public Dictionary<string, double> FoodSeason { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Defender morale lost per month past the food.</summary>
    public double StarveMorale { get; set; } = 0.15;
    /// <summary>Chance per month past the food, per such month, that the place surrenders.</summary>
    public double SurrenderPerMonth { get; set; } = 0.1;

    // The besiegers.
    /// <summary>Supply the besiegers lose per month (twice in winter).</summary>
    public double SupplyDecay { get; set; } = 0.02;
    /// <summary>Multiplier on the supply decay in winter months.</summary>
    public double WinterDecay { get; set; } = 2;
    /// <summary>The siege is lifted when the besiegers' supply falls below this.</summary>
    public double LiftBelow { get; set; } = 0.25;
    /// <summary>Share of the besiegers disease takes per month.</summary>
    public double Disease { get; set; } = 0.02;
    /// <summary>Multiplier on disease in a marsh camp or a summer month.</summary>
    public double DiseaseRife { get; set; } = 2;

    // Storming.
    /// <summary>Storming is tried when <c>SiegeSkill/100 × Pa / (Pd(1 + walls))</c> exceeds this.</summary>
    public double StormRatio { get; set; } = 1.2;
    /// <summary>Monthly chance to try a storm, times <c>SiegeSkill/100</c>.</summary>
    public double StormChance { get; set; } = 0.15;
    /// <summary>Multiplier on storm power when the besiegers field siege engines (a siege tactic of their doctrine).</summary>
    public double EnginePower { get; set; } = 1.3;
    /// <summary>Share of the besiegers a failed storm costs.</summary>
    public double StormLoss { get; set; } = 0.1;
    /// <summary>Share of a stormed place's people who die, at least.</summary>
    public double StormedCasualtyMin { get; set; } = 0.1;
    /// <summary>At most.</summary>
    public double StormedCasualtyMax { get; set; } = 0.3;

    // Betrayal.
    /// <summary>Chance per month that someone opens a gate.</summary>
    public double BetrayalBase { get; set; } = 0.01;
    /// <summary>Added when the besiegers' spymaster's intrigue is above <see cref="BetrayalIntrigue"/>.</summary>
    public double BetrayalSpy { get; set; } = 0.03;
    /// <summary>See <see cref="BetrayalSpy"/>.</summary>
    public double BetrayalIntrigue { get; set; } = 70;
    /// <summary>Added while the place starves.</summary>
    public double BetrayalStarving { get; set; } = 0.04;

    // Relief.
    /// <summary>A relief army marches when the defender's free levy is at least this share of the besiegers.</summary>
    public double ReliefShare { get; set; } = 0.35;
    /// <summary>Degrees a relief army covers per month.</summary>
    public double ReliefDegreesPerMonth { get; set; } = 6;
    /// <summary>Defender morale lost when a relief army is beaten or turns back.</summary>
    public double ReliefFailedMorale { get; set; } = 0.15;

    // What follows.
    /// <summary>Grudge the besiegers hold against a place that held.</summary>
    public int FailedGrudge { get; set; } = 1;
    /// <summary>Turns of tribute a fallen capital pays (a non-aggression window).</summary>
    public int TributeTurns { get; set; } = 2;
    /// <summary>Most of a besieged place's people the siege's dead may be.</summary>
    public double CasualtyCap { get; set; } = 0.15;
}
