using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class CultureDriftTests
{
    private static PeoplesStore Shipped => PeoplesStore.Load();

    [Fact]
    public void TurnZero_IsPreDrift()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "dwarven_clan", CultureDelta.Subsistence, "artisan", "agrarian", "settle"),
        });
        var resolved = ethnos.Resolve(Shipped, 0);
        Assert.Equal("artisan", resolved.Subsistence);
    }

    [Fact]
    public void SubsistenceDelta_AppliesFromItsTurn()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "dwarven_clan", CultureDelta.Subsistence, "artisan", "agrarian", "settle"),
        });
        Assert.Equal("agrarian", ethnos.Resolve(Shipped, 1).Subsistence);
        Assert.Equal("agrarian", ethnos.Resolve(Shipped, 9).Subsistence);
    }

    [Fact]
    public void LaterDelta_WinsAndFromIsProvenanceOnly()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "dwarven_clan", CultureDelta.Subsistence, "artisan", "agrarian", "settle"),
            // A wrong From does not block: From documents, it never gates.
            new CultureDelta(2, "dwarven_clan", "subsistence", "pastoral", "maritime", "harbor"),
        });
        Assert.Equal("maritime", ethnos.Resolve(Shipped, 2).Subsistence);
    }

    [Fact]
    public void TargetFilters_StarFolkAndCultureMatch()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "human_common", CultureDelta.Honor, "50", "10", "trade"),
            new CultureDelta(1, "dwarf", CultureDelta.Honor, "80", "70", "oath"),
            new CultureDelta(2, "*", CultureDelta.Collectivism, "80", "60", "settle"),
        });
        var resolved = ethnos.Resolve(Shipped, 2);
        // The human_common delta never touches a dwarven ethnos; the folk and star ones do.
        Assert.Equal(70, resolved.Honor);
        Assert.Equal(60, resolved.Collectivism);
        Assert.Equal(70, ethnos.Resolve(Shipped, 1).Honor);
        Assert.Equal(80, ethnos.Resolve(Shipped, 1).Collectivism);
    }

    [Fact]
    public void LanguageDelta_SwitchesNamingTable()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(2, "dwarven_clan", CultureDelta.Language, "dwarven", "fantasy_common", "trade-tongue"),
        });
        Assert.Equal("dwarven", ethnos.Resolve(Shipped, 1).NamingTable);
        Assert.Equal("fantasy_common", ethnos.Resolve(Shipped, 2).NamingTable);
    }

    [Fact]
    public void ValuesFaithLineageAndMixed_Drift()
    {
        var ethnos = new Ethnos("human", "human_common", new[]
        {
            new CultureDelta(1, "*", CultureDelta.Xenophobia, "30", "65", "siege"),
            new CultureDelta(1, "*", "hierarchyClaim", "40", "75", "siege"),
            new CultureDelta(1, "*", CultureDelta.Faith, "polytheism", "ancestor", "conquest"),
            new CultureDelta(1, "*", CultureDelta.Lineage, "Patrilineal", "Matrilineal", "conquest"),
            new CultureDelta(1, "*", CultureDelta.Mixed, "Hybrid", "Dominant", "conquest"),
        });
        var resolved = ethnos.Resolve(Shipped, 1);
        Assert.Equal(65, resolved.Xenophobia);
        Assert.Equal(75, resolved.HierarchyClaim);
        Assert.Equal("ancestor", resolved.FaithForm);
        Assert.Equal("Matrilineal", resolved.LineageDefault);
        Assert.Equal("Dominant", resolved.MixedNaming);
    }

    [Fact]
    public void Resolve_DoesNotMutateShippedTables()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "*", CultureDelta.Subsistence, "artisan", "agrarian", "settle"),
        });
        ethnos.Resolve(Shipped, 5);
        Assert.Equal("artisan", Shipped.GetCulture("dwarven_clan").Subsistence);
    }

    [Fact]
    public void Resolve_IsDeterministic()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(2, "*", CultureDelta.Subsistence, "x", "agrarian", "settle"),
            new CultureDelta(1, "*", CultureDelta.Honor, "80", "40", "settle"),
        });
        var a = ethnos.Resolve(Shipped, 3);
        var b = ethnos.Resolve(Shipped, 3);
        Assert.Equal(a.Subsistence, b.Subsistence);
        Assert.Equal(a.Honor, b.Honor);
        Assert.Equal(a.NamingTable, b.NamingTable);
    }

    [Theory]
    [InlineData("phlogiston", "fire")]
    [InlineData("xenophobia", "101")]
    [InlineData("xenophobia", "-1")]
    [InlineData("xenophobia", "many")]
    [InlineData("lineage", "sideways")]
    [InlineData("mixed", "melting-pot")]
    [InlineData("threshold", "0.4")]
    public void BadTraitOrValue_Throws(string trait, string to)
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "*", trait, "", to, "test"),
        });
        Assert.Throws<ArgumentException>(() => ethnos.Resolve(Shipped, 1));
    }

    [Fact]
    public void Delta_RejectsBlankTargetTraitAndTo()
    {
        Assert.Throws<ArgumentException>(() => new CultureDelta(1, "", "subsistence", "a", "b"));
        Assert.Throws<ArgumentException>(() => new CultureDelta(1, "*", "", "a", "b"));
        Assert.Throws<ArgumentException>(() => new CultureDelta(1, "*", "subsistence", "a", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CultureDelta(-1, "*", "subsistence", "a", "b"));
    }

    [Fact]
    public void Resolve_RejectsNullStoreNegativeTurnAndUnknownCulture()
    {
        var ethnos = new Ethnos("dwarf", "dwarven_clan");
        Assert.Throws<ArgumentNullException>(() => ethnos.Resolve(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ethnos.Resolve(Shipped, -1));
        Assert.Throws<UnknownPeoplesKeyException>(() => new Ethnos("dwarf", "troll_clan").Resolve(Shipped, 1));
        Assert.Throws<ArgumentException>(() => new Ethnos("", "dwarven_clan"));
    }
}
