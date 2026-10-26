namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// Trade and its people (<c>Peoples/Data/Commerce/default.yaml</c>; the history reads <c>default</c>): how busy routes get, where
/// merchant princes rise and how they grow rich, what their pledges buy in war, smuggling across severed roads, and privateers.
/// Wealth is in abstract units; a new merchant starts with <see cref="MerchantWealth"/>.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class CommerceDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;

    // Traffic.
    /// <summary>Traffic a safe route draws before its merchants.</summary>
    public double TrafficBase { get; set; } = 0.6;
    /// <summary>Traffic the route's merchants add at <see cref="WealthScale"/> wealth.</summary>
    public double TrafficWealth { get; set; } = 0.4;
    /// <summary>Merchant wealth at which their pull on traffic is full.</summary>
    public double WealthScale { get; set; } = 100;
    /// <summary>Share of the gap to its level a route's traffic closes each turn.</summary>
    public double TrafficDrift { get; set; } = 0.3;
    /// <summary>Share of its traffic a severed route keeps (smugglers).</summary>
    public double SeveredTraffic { get; set; } = 0.1;

    // Merchant princes.
    /// <summary>Per-turn chance a settlement that may hold one more merchant gains one.</summary>
    public double MerchantChance { get; set; } = 0.2;
    /// <summary>Smallest settlement tier that holds merchants (<c>Town</c>).</summary>
    public string MerchantTier { get; set; } = "Town";
    /// <summary>A settlement within this angle of an active road (or a coastal one) is on a route.</summary>
    public double MerchantRouteDegrees { get; set; } = 4;
    /// <summary>Merchants a settlement holds at most.</summary>
    public int MaxMerchantsPerSettlement { get; set; } = 2;
    /// <summary>Merchants a polity holds at most.</summary>
    public int MaxMerchantsPerPolity { get; set; } = 2;
    /// <summary>No merchant or privateer is created within this many of the world's person cap (the court keeps the rest).</summary>
    public int PersonReserve { get; set; } = 512;
    /// <summary>Living places (of the per-polity person cap) left free for the court.</summary>
    public int CourtReserve { get; set; } = 2;
    /// <summary>Wealth of a new merchant.</summary>
    public double MerchantWealth { get; set; } = 10;
    /// <summary>Wealth a merchant gains per turn at full traffic on its route, × (0.5 + Stewardship/100).</summary>
    public double WealthGrowth { get; set; } = 8;
    /// <summary>A new merchant is this share of a lifespan old, at least ...</summary>
    public double AgeMin { get; set; } = 0.25;
    /// <summary>... and at most this.</summary>
    public double AgeMax { get; set; } = 0.5;

    // War.
    /// <summary>A merchant pledges this share of its wealth above <see cref="PledgeFloor"/> while its polity holds a grudge of at least <see cref="PledgeGrudge"/> against a neighbour.</summary>
    public double PledgeShare { get; set; } = 0.3;
    /// <summary>See <see cref="PledgeShare"/>.</summary>
    public int PledgeGrudge { get; set; } = 1;
    /// <summary>A merchant pledges only from wealth above this (it never pledges itself into ruin).</summary>
    public double PledgeFloor { get; set; } = 10;
    /// <summary>Mercenaries a unit of pledged wealth hires (the next campaign spends every pledge).</summary>
    public double MercenaryPerWealth { get; set; } = 2.0;
    /// <summary>Mercenaries are at most this share of the levy.</summary>
    public double MercenaryCap { get; set; } = 0.5;

    // Smuggling and privateering.
    /// <summary>Per-turn chance a severed route with merchants at both ends is smuggled across.</summary>
    public double SmuggleChance { get; set; } = 0.3;
    /// <summary>Wealth each smuggling merchant gains.</summary>
    public double SmuggleGain { get; set; } = 5;
    /// <summary>Grudge a seafaring polity must hold against a coastal rival to commission a privateer.</summary>
    public int PrivateerGrudge { get; set; } = 2;
    /// <summary>Per-turn chance it commissions one.</summary>
    public double PrivateerChance { get; set; } = 0.2;
    /// <summary>Per-turn chance a privateer takes a prize.</summary>
    public double PrivateerRaidChance { get; set; } = 0.5;
    /// <summary>Share of the victim merchant's wealth a prize takes (the privateer keeps it).</summary>
    public double PrivateerLoot { get; set; } = 0.2;
    /// <summary>The rival's coastal settlement lies within this angle of the polity's own coast.</summary>
    public double PrivateerDegrees { get; set; } = 20;
    /// <summary>Privateers a polity commissions at most.</summary>
    public int MaxPrivateers { get; set; } = 1;
    /// <summary>A merchant poorer than this is ruined (and may turn outlaw).</summary>
    public double RuinBelow { get; set; } = 2;
}
