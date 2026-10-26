namespace RoguelikeToolkit.World.Core.History;

/// <summary>The built-in history ruleset: the eleven operators and two post-passes the default world runs.</summary>
public static class StandardHistory
{
    /// <summary>Registers the standard operators and post-passes on <paramref name="driver"/>.</summary>
    public static HistoryDriver Register(HistoryDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        return driver
            .AddOperator(new PopulationOperator())
            .AddOperator(new SettleOperator())
            .AddOperator(new TierOperator())
            .AddOperator(new HazardOperator())
            .AddOperator(new CourtOperator())
            .AddOperator(new LogisticsOperator())
            .AddOperator(new FortificationOperator())
            .AddOperator(new TradeRoadOperator())
            .AddOperator(new HolySiteOperator())
            .AddOperator(new KhanOperator())
            .AddOperator(new TreatyOperator())
            .AddOperator(new WarOperator())
            .AddOperator(new ConquestOperator())
            .AddOperator(new RebellionOperator())
            .AddOperator(new CollapseOperator())
            .AddOperator(new AbsorbOperator())
            .AddOperator(new VillainOperator())
            .AddOperator(new OutlawOperator())
            .AddOperator(new IntrigueOperator())
            .AddOperator(new MerchantOperator())
            .AddOperator(new CommerceOperator())
            .AddOperator(new ExchangeOperator())
            .AddPostPass(new FaithPostPass())
            .AddPostPass(new CityHistoryProjectPostPass());
    }
}
