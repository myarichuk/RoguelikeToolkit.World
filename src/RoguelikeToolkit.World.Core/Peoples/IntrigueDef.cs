namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// Intrigue and the court's sway (<c>Peoples/Data/Intrigue/default.yaml</c>; the history reads <c>default</c>): how hawkish or
/// dovish a court leans and what that does to war and treaty chances, the plots spymasters run against rivals (sponsoring
/// outlaws, sabotage, bribes, assassination) and how they are exposed, and how loyal office holders are.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class IntrigueDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;

    // The court's sway.
    /// <summary>War chance × <c>WarBase + WarHawk × hawk</c>; hawk = the court's mean <c>(100 − A + E)/200</c> (0..1).</summary>
    public double WarBase { get; set; } = 0.7;
    /// <summary>See <see cref="WarBase"/>.</summary>
    public double WarHawk { get; set; } = 0.6;
    /// <summary>Treaty acceptance × <c>TreatyBase + TreatyDove × dove</c>; dove = 1 − hawk, the mean over both courts.</summary>
    public double TreatyBase { get; set; } = 0.7;
    /// <summary>See <see cref="TreatyBase"/>.</summary>
    public double TreatyDove { get; set; } = 0.6;
    /// <summary>Weight of the ruler in the court's mean (each office holder weighs 1).</summary>
    public double RulerWeight { get; set; } = 2;
    /// <summary>A grudge this heavy against a neighbour is a casus belli: the attacker marches on it rather than a random neighbour ...</summary>
    public int CasusBelliGrudge { get; set; } = 3;
    /// <summary>... with this much added to the war chance.</summary>
    public double CasusBelliChance { get; set; } = 0.1;
    /// <summary>War chance × this when the attacker's merchants trade on a route to the defender.</summary>
    public double MerchantPeace { get; set; } = 0.6;

    // Plots.
    /// <summary>Per-turn chance a polity with a spymaster plots against a rival it holds a grudge against, × Intrigue/100.</summary>
    public double PlotChance { get; set; } = 0.3;
    /// <summary>
    /// Weight of each plot a court starts against a rival: <c>sponsorship</c> (pay or hire the band on a road of the rival's) and
    /// <c>spy_network</c> (plant an agent in the rival's seat). Every covert act after that has a sponsor and an operative.
    /// </summary>
    public Dictionary<string, double> Plots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Weight of each operation an agent runs (<c>sabotage</c>, <c>bribe</c>, <c>assassination</c>, <c>theft</c>).</summary>
    public Dictionary<string, double> AgentOps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per-turn chance an agent runs an operation, × (0.5 + Intrigue/100).</summary>
    public double AgentOpChance { get; set; } = 0.4;
    /// <summary>Per-turn chance the rival's counter-intelligence uncovers an idle agent, × the rival's spy/100.</summary>
    public double AgentDiscovery { get; set; } = 0.05;
    /// <summary>A new agent is this share of a lifespan old, at least ...</summary>
    public double AgentAgeMin { get; set; } = 0.25;
    /// <summary>... and at most this.</summary>
    public double AgentAgeMax { get; set; } = 0.5;
    /// <summary>Agents a polity runs at most (one per rival).</summary>
    public int MaxAgents { get; set; } = 2;
    /// <summary>Share of the rival's richest merchant's wealth a theft takes (to the agent's purse).</summary>
    public double TheftShare { get; set; } = 0.2;
    /// <summary>Per-turn chance a sponsored band cuts the road it works on its sponsor's account.</summary>
    public double SponsoredSabotage { get; set; } = 0.15;
    /// <summary>Offices whose holder runs a polity's intrigue (and watches for its rivals'), the first held first.</summary>
    public List<string> PlotOffices { get; set; } = new();
    /// <summary>Intrigue of a polity holding none of <see cref="PlotOffices"/>.</summary>
    public double NoSpymaster { get; set; } = 30;
    /// <summary>An agent's operation is exposed (the agent caught and executed, the sponsor named) with <c>ExposeBase + ExposeSpy × (victim's spy − agent's Intrigue)/100</c>.</summary>
    public double ExposeBase { get; set; } = 0.25;
    /// <summary>See <see cref="ExposeBase"/>.</summary>
    public double ExposeSpy { get; set; } = 0.5;
    /// <summary>Per raid by a sponsored band, the victim exposes the sponsor with <c>SponsorExposeBase + SponsorExposeSpy × spy/100</c>.</summary>
    public double SponsorExposeBase { get; set; } = 0.2;
    /// <summary>See <see cref="SponsorExposeBase"/>.</summary>
    public double SponsorExposeSpy { get; set; } = 0.4;
    /// <summary>Chance a hanged leader of a sponsored band names the sponsor.</summary>
    public double ConfessChance { get; set; } = 0.5;
    /// <summary>Grudge an exposed plot gives the victim against the plotter.</summary>
    public int ExposedGrudge { get; set; } = 3;
    /// <summary>Assassination succeeds with <c>AssassinBase + AssassinSpy × (agent's Intrigue − victim's spy)/100</c>.</summary>
    public double AssassinBase { get; set; } = 0.35;
    /// <summary>See <see cref="AssassinBase"/>.</summary>
    public double AssassinSpy { get; set; } = 0.5;
    /// <summary>Loyalty a bribe takes from the rival's general.</summary>
    public int BribeLoyalty { get; set; } = 40;

    // Loyalty.
    /// <summary>Loyalty office holders drift toward before their bonds and temper.</summary>
    public double LoyaltyBase { get; set; } = 20;
    /// <summary>Loyalty a patron bond with the ruler adds.</summary>
    public double PatronLoyalty { get; set; } = 30;
    /// <summary>Loyalty each open rivalry takes.</summary>
    public double RivalLoyalty { get; set; } = 15;
    /// <summary>Loyalty per point of agreeableness above 50 (below takes).</summary>
    public double AgreeablenessLoyalty { get; set; } = 0.6;
    /// <summary>Share of the gap to that level closed each turn.</summary>
    public double LoyaltyDrift { get; set; } = 0.3;
}
