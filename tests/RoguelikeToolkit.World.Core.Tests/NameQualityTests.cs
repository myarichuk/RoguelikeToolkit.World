using RoguelikeToolkit.World.Core.Names;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Guards the quality of what the name tables actually produce, across every shipped style.</summary>
public sealed class NameQualityTests
{
    private static readonly string[] Kinds =
        { "village", "town", "city", "river", "lake", "mountain", "hill", "valley", "forest", "hold", "camp" };

    [Fact]
    public void ShippedYaml_MatchesTheSchemaStrictly()
    {
        // The loader ignores unknown keys, which hides typos and comma-in-gloss mistakes
        // ("{Gloss: red, beautiful}" silently drops "beautiful"). Shipped files must be exact.
        var strict = new DeserializerBuilder().WithNamingConvention(NullNamingConvention.Instance).Build();
        var asm = typeof(NameDataStore).Assembly;
        var names = asm.GetManifestResourceNames().Where(n => n.Contains(".Names.Data.") && n.EndsWith(".yaml")).ToList();
        Assert.NotEmpty(names);
        foreach (var n in names)
        {
            using var r = new StreamReader(asm.GetManifestResourceStream(n)!);
            var ex = Record.Exception(() => strict.Deserialize<CultureData>(r.ReadToEnd()));
            Assert.True(ex is null, $"{n}: {ex?.Message}");
        }
    }

    [Fact]
    public void Styles_ListEveryTable_AndEachOneGenerates()
    {
        var styles = FantasyNameGenerator.Styles();
        Assert.Contains(styles, s => s.Race == FantasyRaces.Human && s.Culture == Cultures.Hebrew);
        Assert.Contains(styles, s => s.Race == FantasyRaces.Orc);
        Assert.Equal(styles.Count, styles.Select(s => s.Label).Distinct().Count());
        Assert.All(styles, s => Assert.False(string.IsNullOrWhiteSpace(s.Label)));
        foreach (var s in styles)
        {
            Assert.False(string.IsNullOrWhiteSpace(FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, 7).Text));
            Assert.False(string.IsNullOrWhiteSpace(
                FantasyNameGenerator.GeneratePlace("town", new PlaceContext(), s.Race, s.Culture, 7).Text));
        }
    }

    [Fact]
    public void AvailableRaces_IncludesHuman_AndNoCultureTables()
    {
        var races = FantasyNameGenerator.AvailableRaces().ToList();
        Assert.Contains(FantasyRaces.Human, races, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(races, r => r.StartsWith("human_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Cultures.FantasyCommon, races);
    }

    [Fact]
    public void ThemeDropdown_EveryOfferedTheme_ActuallyBiasesNames()
    {
        foreach (var s in FantasyNameGenerator.Styles())
        {
            foreach (var feminine in new[] { false, true })
                foreach (var theme in FantasyNameGenerator.PersonThemes(s.Race, s.Culture, feminine: feminine))
                {
                    bool hit = Enumerable.Range(1, 120).Any(seed =>
                        FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, feminine: feminine, inspiredBy: new[] { theme })
                            .Inspirations.Contains(theme));
                    Assert.True(hit, $"{s.Label} (feminine={feminine}): person theme '{theme}' never shows up in 120 names.");
                }
            foreach (var theme in FantasyNameGenerator.PlaceThemes(s.Race, s.Culture))
            {
                bool hit = Enumerable.Range(1, 120).Any(seed => Kinds.Any(k =>
                    FantasyNameGenerator.GeneratePlace(k, new PlaceContext(), s.Race, s.Culture, seed, inspiredBy: new[] { theme })
                        .Inspirations.Contains(theme)));
                Assert.True(hit, $"{s.Label}: place theme '{theme}' never shows up.");
            }
        }
    }

    [Fact]
    public void Gender_PicksMatchingGivenNames_ForGenderedTables()
    {
        var store = FantasyNameGenerator.Shared;
        foreach (var culture in new[] { Cultures.Hebrew, Cultures.Russian, Cultures.Arabic, Cultures.Japanese, Cultures.Irish })
        {
            var table = store.Get(culture);
            var fem = table.GivenStems.Where(m => m.Tags.Contains("fem")).Select(m => m.Form).ToHashSet();
            var masc = table.GivenStems.Where(m => m.Tags.Contains("masc")).Select(m => m.Form).ToHashSet();
            Assert.NotEmpty(fem);
            Assert.NotEmpty(masc);
            for (int seed = 1; seed <= 60; seed++)
            {
                var f = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed, feminine: true, script: NameScript.Native);
                var m = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed, feminine: false, script: NameScript.Native);
                Assert.DoesNotContain(masc, x => f.Parts[0].Form.Equals(x, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(fem, x => m.Parts[0].Form.Equals(x, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Russian_PatronymicsAndSurnames_AgreeWithGender()
    {
        for (int seed = 1; seed <= 80; seed++)
        {
            var f = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Russian, seed, feminine: true, script: NameScript.Latin);
            var m = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Russian, seed, feminine: false, script: NameScript.Latin);
            var fw = f.Text.Split(' ');
            var mw = m.Text.Split(' ');
            Assert.EndsWith("ovna", fw[1]);
            Assert.EndsWith("ovich", mw[1]);
            Assert.True(fw[2].EndsWith('a'), f.Text);
            Assert.False(mw[2].EndsWith('a'), m.Text);
        }
    }

    [Fact]
    public void Hebrew_GivenNames_AreWholeAuthoredNames()
    {
        var table = FantasyNameGenerator.Shared.Get(Cultures.Hebrew);
        var known = table.GivenStems.Select(g => g.Form).ToHashSet();
        for (int seed = 1; seed <= 100; seed++)
        {
            var n = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Hebrew, seed);
            Assert.Contains(n.Parts[0].Form, known);
            Assert.True(Transliterator.IsHebrew(n.Text));
        }
    }

    [Fact]
    public void NoStutters_AcrossEveryStyleAndScript()
    {
        foreach (var script in new[] { NameScript.Native, NameScript.Latin, NameScript.Hebrew, NameScript.Cyrillic })
            foreach (var s in FantasyNameGenerator.Styles())
                for (int seed = 1; seed <= 60; seed++)
                {
                    var texts = new List<string>
                    {
                        FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, feminine: seed % 2 == 0, script: script).Text,
                        FantasyNameGenerator.GeneratePlace(Kinds[seed % Kinds.Length], new PlaceContext { NearWater = seed % 3 == 0 }, s.Race, s.Culture, seed, script: script).Text,
                    };
                    foreach (var t in texts)
                    {
                        Assert.DoesNotMatch(@"(\p{L})\1\1", t);          // no letter three times in a row
                        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < words.Length; i++)
                            Assert.False(words[i].Equals(words[i - 1], StringComparison.OrdinalIgnoreCase), $"{s.Label}/{script}: '{t}'");
                        Assert.DoesNotContain("  ", t);
                        Assert.Equal(t.Trim(), t);
                    }
                }
    }

    [Fact]
    public void HebrewScript_NeverPutsFinalFormsMidWord()
    {
        foreach (var s in FantasyNameGenerator.Styles())
            for (int seed = 1; seed <= 60; seed++)
            {
                var t = FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, script: NameScript.Hebrew).Text
                        + " " + FantasyNameGenerator.GeneratePlace("town", new PlaceContext(), s.Race, s.Culture, seed, script: NameScript.Hebrew).Text;
                Assert.DoesNotMatch("[םןףץך][א-ת]", t);
            }
    }

    [Fact]
    public void HebrewTransliteration_FollowsModernSpelling()
    {
        Assert.Equal("קרים", Transliterator.ToHebrew("Karim"));
        Assert.Equal("דנה", Transliterator.ToHebrew("Dana"));
        Assert.Equal("בן", Transliterator.ToHebrew("Ben"));
        Assert.Equal("ולדימיר", Transliterator.ToHebrew("Vladimir"));
        Assert.Equal("אל-דין", Transliterator.ToHebrew("al-Din"));   // each word is handled on its own
        Assert.Equal("Бен Давид", Transliterator.ToCyrillic("Ben David"));
    }

    [Fact]
    public void LanguageTables_DoNotLeakFantasyCommonVocabulary_IntoPlaceNames()
    {
        var common = FantasyNameGenerator.Shared.Get(Cultures.FantasyCommon);
        var banned = common.Descriptors.Concat(common.Nouns).Select(m => m.Form.ToLowerInvariant()).ToHashSet();
        foreach (var culture in new[] { Cultures.Hebrew, Cultures.Japanese, Cultures.Chinese, Cultures.Russian, Cultures.Arabic })
            for (int seed = 1; seed <= 40; seed++)
            {
                var p = FantasyNameGenerator.GeneratePlace("village", new PlaceContext(), FantasyRaces.Human, culture, seed, script: NameScript.Latin);
                foreach (var part in p.Parts)
                    Assert.DoesNotContain(part.Form.ToLowerInvariant(), banned.Except(new[] { "water" }));
            }
    }

    [Fact]
    public void PlaceNouns_FitTheKindOfPlace()
    {
        // A river is never a "-hold" and a hold never a "-brook".
        for (int seed = 1; seed <= 60; seed++)
        {
            var river = FantasyNameGenerator.GeneratePlace("river", new PlaceContext(), FantasyRaces.Dwarf, Cultures.FantasyCommon, seed);
            Assert.DoesNotContain(river.Parts, p => p.Role == NamePartRoles.Geography && p.Form is "Hold" or "Delve" or "Bar");
            var hold = FantasyNameGenerator.GeneratePlace("hold", new PlaceContext(), FantasyRaces.Dwarf, Cultures.FantasyCommon, seed);
            Assert.Contains(hold.Parts, p => p.Role == NamePartRoles.Geography && p.Form is "Hold" or "Bar" or "Burg" or "Burgs");
        }
    }

    [Fact]
    public void EnsureExtracted_UpgradesUntouchedFiles_ButNeverUserEdits()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            NameDataStore.EnsureExtracted(dir);
            var edited = Path.Combine(dir, "dwarven.yaml");
            var untouched = Path.Combine(dir, "orcish.yaml");
            var shippedOrc = File.ReadAllText(untouched);
            var shippedDwarf = File.ReadAllText(edited);

            // Simulate: orcish was shipped older (manifest knows that hash), dwarven was edited by the user.
            File.WriteAllText(untouched, "Key: orcish\nOLD\n");
            var manifest = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir, ".shipped.json")))!;
            manifest["orcish.yaml"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(untouched)));
            File.WriteAllText(Path.Combine(dir, ".shipped.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
            File.WriteAllText(edited, shippedDwarf + "\n# my tweak\n");

            NameDataStore.EnsureExtracted(dir);
            Assert.Equal(shippedOrc, File.ReadAllText(untouched));                 // refreshed
            Assert.Contains("# my tweak", File.ReadAllText(edited));               // preserved

            // A pre-manifest copy is upgraded, with the old content kept alongside.
            File.Delete(Path.Combine(dir, ".shipped.json"));
            File.WriteAllText(edited, "Key: dwarven\nLEGACY\n");
            NameDataStore.EnsureExtracted(dir);
            Assert.Equal(shippedDwarf, File.ReadAllText(edited));
            Assert.Contains("LEGACY", File.ReadAllText(edited + ".old"));
            Assert.NotNull(NameDataStore.Load(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
