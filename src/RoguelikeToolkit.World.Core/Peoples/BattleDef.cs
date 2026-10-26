namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// How battles are resolved (<c>Peoples/Data/Battle/default.yaml</c>; the history reads <c>default</c>). Every term of the
/// battle model is here: levies, reach and supply, morale, terrain, initiative, the margin and its bands, losses, commanders'
/// fates, beats, epithets, customs and what each outcome does. Degrees are great-circle angles; shares are 0..1.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class BattleDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;

    // Forces.
    /// <summary>Share of a polity's people under arms, per government form name.</summary>
    public Dictionary<string, double> FormLevy { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Levy multiplier per culture collectivism point above 50 (below 50 lowers it).</summary>
    public double CollectivismLevy { get; set; } = 0.005;
    /// <summary>Reach scale: the attacker's strength falls by <c>exp(-d/R)</c>, <c>R = ReachDegrees(0.5 + Mobility/100)</c>.</summary>
    public double ReachDegrees { get; set; } = 40;
    /// <summary>Muster scale: a settlement <c>d</c> degrees from the field sends <c>exp(-d/M)</c> of its levy, <c>M = MusterDegrees(0.5 + Mobility/100)</c>.</summary>
    public double MusterDegrees { get; set; } = 15;
    /// <summary>Share of its levy a side brings to any field however far its people live (the rest musters by distance).</summary>
    public double MusterFloor { get; set; } = 0.5;
    /// <summary>Share of its levy an unwarned defender musters; a warned one musters all.</summary>
    public double DefenderReadiness { get; set; } = 0.6;
    /// <summary>Chance the defender is warned with equal spymasters.</summary>
    public double WarnedBase { get; set; } = 0.5;
    /// <summary>Change in that chance per point of spymaster intrigue the defender has over the attacker.</summary>
    public double WarnedPerIntrigue { get; set; } = 0.004;

    // Supply.
    /// <summary>Supply carried along the lines per season name.</summary>
    public Dictionary<string, double> SeasonSupply { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Weights of the land's yield channels (<c>Arable</c>, <c>Pasture</c>, <c>Game</c>, <c>Timber</c>, <c>Fish</c>) in what a host can forage.</summary>
    public Dictionary<string, double> ForageChannels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Supply never falls below this.</summary>
    public double SupplyMin { get; set; } = 0.25;
    /// <summary>How much missing supply costs in power: power times <c>1 - SupplyPower(1 - supply)</c>.</summary>
    public double SupplyPower { get; set; } = 0.3;
    /// <summary>An attacker with less supply than this never reaches the field (the campaign is averted).</summary>
    public double AvertedBelow { get; set; } = 0.35;

    // Morale.
    /// <summary>Morale before any term.</summary>
    public double MoraleBase { get; set; } = 0.5;
    /// <summary>Morale per point of culture honor.</summary>
    public double MoraleHonor { get; set; } = 0.003;
    /// <summary>Morale of a side fighting on its own soil.</summary>
    public double MoraleHome { get; set; } = 0.12;
    /// <summary>Morale of a side fighting on its own holy ground.</summary>
    public double MoraleHoly { get; set; } = 0.1;
    /// <summary>Morale from the commander's renown, times <c>tanh(renown/RenownScale)</c>.</summary>
    public double MoraleRenown { get; set; } = 0.05;
    /// <summary>Renown scale of that term.</summary>
    public double RenownScale { get; set; } = 10;
    /// <summary>Morale lost per unit of crowding pressure above <see cref="PressureFrom"/>.</summary>
    public double MoralePressure { get; set; } = 0;
    /// <summary>Pressure a side bears before it hurts morale.</summary>
    public double PressureFrom { get; set; } = 0.5;
    /// <summary>Morale lost per unit of missing supply.</summary>
    public double MoraleHunger { get; set; } = 0.1;
    /// <summary>Lowest morale.</summary>
    public double MoraleMin { get; set; } = 0.2;
    /// <summary>Highest morale.</summary>
    public double MoraleMax { get; set; } = 1.2;

    // Terrain.
    /// <summary>Terrain class names to the defender's bonus there; the keys are the terrain vocabulary.</summary>
    public Dictionary<string, double> TerrainDefence { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Biome name to terrain class; an unlisted biome is <see cref="DefaultTerrain"/>.</summary>
    public Dictionary<string, string> BiomeTerrain { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Terrain class of an unlisted biome.</summary>
    public string DefaultTerrain { get; set; } = "open";
    /// <summary>Terrain class of default ground at or above <see cref="HillsFrom"/>.</summary>
    public string HillsTerrain { get; set; } = "hills";
    /// <summary>Height (sea level 0, mountains from 0.5) from which default ground counts as hills.</summary>
    public double HillsFrom { get; set; } = 0.3;
    /// <summary>Defender's bonus for a river the attacker must cross.</summary>
    public double RiverDefence { get; set; } = 0.3;
    /// <summary>Chance per point of mobility (over 100) that an attacker who won the manoeuvre fords elsewhere.</summary>
    public double RiverBypass { get; set; } = 0.5;
    /// <summary>Frontier tiles offered as battlefields.</summary>
    public int FieldCandidates { get; set; } = 6;
    /// <summary>Exponent on the manoeuvre winner's preference when it picks among them.</summary>
    public double FieldSharpness { get; set; } = 2;

    // Initiative.
    /// <summary>Initiative per point of mobility.</summary>
    public double InitiativeMobility { get; set; } = 0.01;
    /// <summary>Initiative per point of the commander's martial skill.</summary>
    public double InitiativeMartial { get; set; } = 0.01;
    /// <summary>Initiative per point of the commander's intrigue.</summary>
    public double InitiativeIntrigue { get; set; } = 0.003;
    /// <summary>Spread of the initiative noise (a triangular draw of this half-width).</summary>
    public double InitiativeNoise { get; set; } = 0.5;

    // Engagement.
    /// <summary>Margin per unit of log power ratio (2 gives the Lanchester square law).</summary>
    public double MarginScale { get; set; } = 2;
    /// <summary>Spread of the margin's logistic noise.</summary>
    public double MarginSpread { get; set; } = 1;
    /// <summary>Smallest spread after epithets.</summary>
    public double MarginSpreadMin { get; set; } = 0.3;
    /// <summary>Margin beyond which the loser is routed.</summary>
    public double DecisiveMargin { get; set; } = 3.25;
    /// <summary>Margin beyond which the winner clearly holds the field.</summary>
    public double VictoryMargin { get; set; } = 0.6;
    /// <summary>Margin within which neither side breaks.</summary>
    public double StalemateMargin { get; set; } = 0.15;
    /// <summary>A side that won the manoeuvre refuses battle when its power is below this share of the enemy's.</summary>
    public double DeclineBelow { get; set; } = 0.4;
    /// <summary>Power per point of the commander's martial skill above 50.</summary>
    public double MartialPower { get; set; } = 0.008;
    /// <summary>A commander whose loyalty is below this betrays the host in battle (only intrigue lowers loyalty).</summary>
    public int BetrayalBelow { get; set; } = -30;
    /// <summary>Share of its power a betrayed host loses.</summary>
    public double BetrayalPower { get; set; } = 0.25;
    /// <summary>Winter penalty on power: this times the field's seasonal range over <see cref="WinterRange"/> (capped at 1) times (1 - cold tolerance/100).</summary>
    public double WinterPenalty { get; set; } = 0.5;
    /// <summary>Seasonal temperature range at which winter bites fully.</summary>
    public double WinterRange { get; set; } = 20;
    /// <summary>Share of the winter penalty a defender in its own land bears.</summary>
    public double WinterDefenderShare { get; set; } = 0.5;
    /// <summary>Loss share winter adds per unit of penalty.</summary>
    public double WinterAttrition { get; set; } = 0.2;

    // Losses and fates.
    /// <summary>Loser's loss share at margin 0.</summary>
    public double LoserLossBase { get; set; } = 0.10;
    /// <summary>Loser's loss share per unit of margin.</summary>
    public double LoserLossPerMargin { get; set; } = 0.08;
    /// <summary>Lowest loser loss share.</summary>
    public double LoserLossMin { get; set; } = 0.05;
    /// <summary>Highest loser loss share before pursuit.</summary>
    public double LoserLossMax { get; set; } = 0.6;
    /// <summary>Winner's loss share is this over (1 + margin).</summary>
    public double WinnerLossBase { get; set; } = 0.12;
    /// <summary>Lowest winner loss share.</summary>
    public double WinnerLossMin { get; set; } = 0.02;
    /// <summary>Highest winner loss share.</summary>
    public double WinnerLossMax { get; set; } = 0.3;
    /// <summary>Pursuit after a rout: this times the winner's mobility/100 times (1 - loser discipline/<see cref="PursuitDiscipline"/>).</summary>
    public double PursuitLoss { get; set; } = 0.15;
    /// <summary>Discipline scale of the pursuit term.</summary>
    public double PursuitDiscipline { get; set; } = 200;
    /// <summary>A commander's chance to fall in any battle.</summary>
    public double GeneralDeathBase { get; set; } = 0.03;
    /// <summary>Added when the commander's side lost.</summary>
    public double GeneralDeathLost { get; set; } = 0.10;
    /// <summary>Added when the commander's side was routed.</summary>
    public double GeneralDeathRouted { get; set; } = 0.15;
    /// <summary>Death cause of a fallen commander.</summary>
    public string GeneralDeathCause { get; set; } = "battle";
    /// <summary>Beats.</summary>
    public List<BattleBeatDef> Beats { get; set; } = new();
    /// <summary>Commander epithet name to its battle effect.</summary>
    public Dictionary<string, BattleEpithetDef> Epithets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Custom name to its effect.</summary>
    public Dictionary<string, BattleCustomDef> Customs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Aftermath.
    /// <summary>
    /// Outcome name (<c>AttackerDecisive</c>...) to what it does to the defender's settlement nearest the field: <c>capture</c>,
    /// <c>raze</c>, <c>sack</c>, <c>grudge</c> (the defender's grudge against the attacker grows by <see cref="AftermathGrudge"/>)
    /// or <c>none</c>. A capital is only ever sacked outright; a capture the attacker cannot hold is a sack. With sieges on,
    /// <see cref="Besiege"/> may send it into a siege first.
    /// </summary>
    public Dictionary<string, string> Aftermath { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// With sieges on: outcome names after which the defender's settlement nearest the field is besieged instead of suffering the
    /// outcome's <see cref="Aftermath"/>, when its walls can stand a siege (otherwise the aftermath applies).
    /// </summary>
    public List<string> Besiege { get; set; } = new();
    /// <summary>With sieges on: a raid the defender refused to meet sends it behind the walls of the place nearest the field.</summary>
    public bool BesiegeDeclined { get; set; } = true;
    /// <summary>Grudge weight of a <c>grudge</c> aftermath.</summary>
    public int AftermathGrudge { get; set; } = 2;
    /// <summary>Share of a sacked settlement's people who die.</summary>
    public double SackCasualty { get; set; } = 0.08;
    /// <summary>Share of a sacked settlement's people who flee.</summary>
    public double SackDisplacement { get; set; } = 0.15;
    /// <summary>Share of a razed settlement's people who die before the rest flee.</summary>
    public double RazeCasualty { get; set; } = 0.3;
    /// <summary>Most of a settlement's people a battle's dead may be (each side's dead fall on its settlement nearest the field).</summary>
    public double CasualtyCap { get; set; } = 0.05;
    /// <summary>Renown of the winning commander.</summary>
    public int RenownWin { get; set; } = 2;
    /// <summary>Further renown for a rout.</summary>
    public int RenownDecisive { get; set; } = 2;
    /// <summary>Renown of the losing commander (usually negative).</summary>
    public int RenownLoss { get; set; } = -1;
    /// <summary>Meetings after which two commanders become nemeses.</summary>
    public int NemesisMeetings { get; set; } = 2;
    /// <summary>Relation kind of nemeses.</summary>
    public string NemesisRelation { get; set; } = "nemesis";
}
