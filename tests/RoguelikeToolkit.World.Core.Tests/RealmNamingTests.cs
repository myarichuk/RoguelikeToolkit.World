using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Names;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Realm titles and name kinds are name data: a people's own entries win per form, then fantasy_common's.</summary>
public class RealmNamingTests
{
    [Fact]
    public void EveryShippedStyle_NamesEveryForm()
    {
        foreach (var style in FantasyNameGenerator.Styles())
            foreach (var form in Enum.GetNames(typeof(PolityForm)))
            {
                var (kinds, titles) = FantasyNameGenerator.RealmNaming(style.Race, style.Culture, form);
                Assert.True(kinds.Count > 0, $"{style.Race}/{style.Culture} {form}: no name kinds");
                Assert.True(titles.Count > 0, $"{style.Race}/{style.Culture} {form}: no titles");
                Assert.All(titles, t => Assert.Contains("{n}", t));
            }
    }

    [Fact]
    public void APeoplesOwnTitles_WinForItsForms()
    {
        var orcHorde = FantasyNameGenerator.RealmNaming("orc", Cultures.FantasyCommon, "Horde").Titles;
        var commonHorde = FantasyNameGenerator.RealmNaming("human", Cultures.FantasyCommon, "Horde").Titles;
        Assert.Contains("{n} Warhost", orcHorde);
        Assert.DoesNotContain("{n} Warhost", commonHorde);
        // A form the orc table does not list falls back to the common titles.
        Assert.Equal(FantasyNameGenerator.RealmNaming("human", Cultures.FantasyCommon, "MerchantRepublic").Titles,
            FantasyNameGenerator.RealmNaming("orc", Cultures.FantasyCommon, "MerchantRepublic").Titles);
    }
}
