namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// The logistics network (<c>Peoples/Data/Logistics/default.yaml</c>; the history reads <c>default</c>): how settlements link
/// (roads, rivers, sea lanes, open country), what a link costs to move a host or a caravan along, how far a polity's hold on
/// its roads reaches, and when it builds waystations. Costs are in road-degrees: one degree of great-circle angle along a safe,
/// open road costs 1. A people's skill at sea is <see cref="WarfareDef.Seafaring"/>.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class LogisticsDef
{
    /// <summary>Profile key, matching the file name; the history reads <c>default</c>.</summary>
    public string Key { get; set; } = string.Empty;

    // Links.
    /// <summary>Cost per degree of a road (route segments between two capitals).</summary>
    public double RoadCost { get; set; } = 1.0;
    /// <summary>A road costs <c>× (1 + RoadUnsafe × (1 − safety))</c>.</summary>
    public double RoadUnsafe { get; set; } = 1.0;
    /// <summary>A severed road costs this many times more.</summary>
    public double SeveredCost { get; set; } = 2.5;
    /// <summary>A road sabotaged (intrigue) within this many turns costs as a severed one.</summary>
    public int SabotagedTurns { get; set; } = 1;
    /// <summary>Each waystation on a road takes this share off its cost (at most <see cref="WaystationsCounted"/> of them).</summary>
    public double WaystationRelief { get; set; } = 0.1;
    /// <summary>Two river settlements this close are linked by the river.</summary>
    public double RiverDegrees { get; set; } = 8;
    /// <summary>Cost per degree along a river.</summary>
    public double RiverCost { get; set; } = 0.8;
    /// <summary>Two coastal settlements this close are linked by a sea lane, for a people at home on the sea.</summary>
    public double SeaDegrees { get; set; } = 20;
    /// <summary>Cost per degree along a sea lane.</summary>
    public double SeaCost { get; set; } = 0.8;
    /// <summary>A people sails its hosts and supply when its <see cref="WarfareDef.Seafaring"/> reaches this.</summary>
    public double SeaFrom { get; set; } = 50;
    /// <summary>Polity forms (<c>MerchantRepublic</c>...) that sail whatever their people.</summary>
    public List<string> SeaForms { get; set; } = new();
    /// <summary>Two settlements this close are linked across open country.</summary>
    public double OverlandDegrees { get; set; } = 10;
    /// <summary>Cost per degree across open country, before the ground.</summary>
    public double OverlandCost { get; set; } = 2.0;
    /// <summary>Ground factor on overland cost per terrain class (the battle model's classes; unlisted classes cost 1).</summary>
    public Dictionary<string, double> TerrainCost { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>A link touching a third party's land costs this many times more.</summary>
    public double ForeignCost { get; set; } = 1.3;
    /// <summary>A link touching the land of a polity the mover fought within <see cref="WarTurns"/> costs this many times more.</summary>
    public double HostileCost { get; set; } = 3.0;
    /// <summary>Two polities count as at war for <see cref="WarTurns"/> turns after a battle between them.</summary>
    public int WarTurns { get; set; } = 3;
    /// <summary>What a settlement of each tier costs as a source of supply (a city feeds a host at once, a hamlet hardly).</summary>
    public Dictionary<string, double> SourceCost { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Source cost of a waystation (a depot, whatever its size).</summary>
    public double WaystationSource { get; set; } = 1.0;

    // War.
    /// <summary>
    /// A host's reach on the network: <c>exp(−cost / (ReachCost × (0.5 + Mobility/100)))</c>. Replaces
    /// <see cref="BattleDef.ReachDegrees"/> with logistics on.
    /// </summary>
    public double ReachCost { get; set; } = 140;
    /// <summary>A winter host whose supply falls below this goes into winter quarters and fights in the spring.</summary>
    public double WinterQuartersBelow { get; set; } = 0.6;
    /// <summary>Share of a wintering host lost (sickness, desertion), less its people's cold tolerance.</summary>
    public double WinterQuartersLoss { get; set; } = 0.12;

    // Roads: the hold of a polity (projection) and what else makes them safe.
    /// <summary>Safety a route drifts toward before anything else.</summary>
    public double BaseSafety { get; set; } = 0.4;
    /// <summary>Safety added by the route keepers' hold on its middle (0..1, the mean over its two ends' polities).</summary>
    public double ProjectionSafety { get; set; } = 0.25;
    /// <summary>A polity's hold on a place fades to nothing at this network cost from its settlements.</summary>
    public double ProjectionCost { get; set; } = 30;
    /// <summary>A polity's hold at full strength per form name; unlisted forms hold at 0.5.</summary>
    public Dictionary<string, double> FormProjection { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Safety each waystation along the route adds (at most <see cref="WaystationsCounted"/>).</summary>
    public double WaystationSafety { get; set; } = 0.05;
    /// <summary>Most waystations counted per route.</summary>
    public int WaystationsCounted { get; set; } = 3;
    /// <summary>Safety lost while the route crosses or joins polities at war.</summary>
    public double BelligerentSafety { get; set; } = 0.3;
    /// <summary>Safety lost while an outlaw band works the route.</summary>
    public double OutlawSafety { get; set; } = 0.25;

    // Waystations.
    /// <summary>A waystation guards (and counts for) a route with a segment end within this angle.</summary>
    public double WaystationDegrees { get; set; } = 4;
    /// <summary>Per-turn chance a keeper builds one on a route that wants one.</summary>
    public double WaystationChance { get; set; } = 0.15;
    /// <summary>A route wants a waystation when it is at least this long (degrees) ...</summary>
    public double WaystationLength { get; set; } = 6;
    /// <summary>... less safe than this ...</summary>
    public double WaystationBelow { get; set; } = 0.6;
    /// <summary>... and has fewer than one waystation per this many degrees.</summary>
    public double WaystationSpacing { get; set; } = 6;
    /// <summary>Waystations a polity holds at most.</summary>
    public int MaxWaystations { get; set; } = 2;
    /// <summary>A waystation stands at least this far from any settlement.</summary>
    public double WaystationClearance { get; set; } = 2;
    /// <summary>Offices, first held first, whose holder's Stewardship scales the chance (<c>× (0.5 + Stewardship/100)</c>); none held: × 1.</summary>
    public List<string> WaystationOffices { get; set; } = new();
}
