using System.Globalization;
using RoguelikeToolkit.World.Core.Names;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Data loading and naming must not depend on the machine's culture: Czech and Welsh collations treat "ch"/"ll"
/// as single letters, which used to reorder YAML load order (alias first-wins) and the style list.
/// </summary>
public class CultureInvarianceTests
{
    private static string Snapshot()
    {
        var store = NameDataStore.Load();
        var peoples = PeoplesStore.Load();
        var lines = new List<string>();
        lines.AddRange(FantasyNameGenerator.Styles(store).Select(s => $"{s.Race}|{s.Culture}|{s.TableKey}|{s.Label}"));
        lines.AddRange(peoples.FolkKeys);
        foreach (var style in FantasyNameGenerator.Styles(store))
            for (int seed = 0; seed < 8; seed++)
                lines.Add(FantasyNameGenerator.GeneratePerson(style.Race, style.Culture, seed).Text);
        return string.Join("\n", lines);
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("cy-GB")]
    [InlineData("tr-TR")]
    public void LoadingAndNaming_IgnoreCurrentCulture(string culture)
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var expected = Snapshot();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(expected, Snapshot());
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}
