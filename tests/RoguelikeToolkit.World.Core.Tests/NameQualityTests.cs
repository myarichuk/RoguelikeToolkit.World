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
        // NameLint is the shared definition of "reads as a mistake" (also shown in the name tester):
        // triple letters, doubled chunks, a word twice, a root repeated across parts, stray spaces.
        var failures = new List<string>();
        foreach (var script in new[] { NameScript.Native, NameScript.Latin, NameScript.Hebrew, NameScript.Cyrillic })
            foreach (var s in FantasyNameGenerator.Styles())
                for (int seed = 1; seed <= 60; seed++)
                {
                    var kind = Kinds[seed % Kinds.Length];
                    var names = new[]
                    {
                        FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, feminine: false, script: script),
                        FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, feminine: true, script: script),
                        FantasyNameGenerator.GeneratePlace(kind, new PlaceContext { NearWater = seed % 3 == 0, Highland = seed % 5 == 0 },
                            s.Race, s.Culture, seed, script: script),
                    };
                    foreach (var n in names)
                        foreach (var problem in NameLint.Check(n))
                            failures.Add($"{s.Label}/{script}: '{n.Text}' ({problem})");
                }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    [Fact]
    public void NameLint_FlagsTheClassicMistakes()
    {
        static GeneratedName N(params (string Form, string Role)[] parts) => new()
        {
            Text = string.Join(" ", parts.Select(p => p.Form)),
            Parts = parts.Select(p => new NamePart { Form = p.Form, Gloss = "x", Role = p.Role }).ToArray(),
        };
        Assert.NotEmpty(NameLint.Check(N(("Ash", NamePartRoles.Given), ("Ashbringer", NamePartRoles.Epithet))));
        Assert.NotEmpty(NameLint.Check(N(("Zaid", NamePartRoles.Given), ("ibn Saqr", NamePartRoles.Patronymic), ("Banu Saqr", NamePartRoles.Family))));
        Assert.NotEmpty(NameLint.Check(N(("Wisp", NamePartRoles.Given), ("Gustgustess", NamePartRoles.Patronymic))));
        Assert.NotEmpty(NameLint.Check(N(("Thrrrun", NamePartRoles.Given))));
        // Particles are not roots: "mac" in "Cormac", "al" in two "al-" names.
        Assert.Empty(NameLint.Check(N(("Cormac", NamePartRoles.Given), ("mac Bran", NamePartRoles.Patronymic))));
        Assert.Empty(NameLint.Check(N(("Karim al-Din", NamePartRoles.Given), ("ibn Hassan", NamePartRoles.Patronymic), ("al-Misri", NamePartRoles.Family))));
    }

    [Fact]
    public void Picks_AreTheSameInEveryScript()
    {
        // One character, four renderings: switching script must not change who they are.
        int differ = 0, total = 0;
        foreach (var s in FantasyNameGenerator.Styles())
            for (int seed = 1; seed <= 30; seed++)
            {
                var native = FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed);
                foreach (var script in new[] { NameScript.Latin, NameScript.Hebrew, NameScript.Cyrillic })
                {
                    total++;
                    var other = FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, script: script);
                    if (!native.Parts.Select(p => p.Gloss).SequenceEqual(other.Parts.Select(p => p.Gloss))) differ++;
                }
            }
        // Only the post-fusion lint reroll (and banned words) may differ by script, and rarely.
        Assert.True(differ * 100 <= total, $"{differ}/{total} names change their picks with the script.");
    }

    [Fact]
    public void Fuse_DropsOnlyDoubledBoundaryVowels()
    {
        Assert.Equal("Shanning", FantasyNameGenerator.Fuse("Shan", "ning", dedupe: true));
        Assert.Equal("Freddochter", FantasyNameGenerator.Fuse("Fred", "dochter", dedupe: true));
        Assert.Equal("Eliel", FantasyNameGenerator.Fuse("Eli", "iel", dedupe: true));
        Assert.Equal("Dobrovich", FantasyNameGenerator.Fuse("Dobro", "ovich", dedupe: true));
        Assert.Equal("Thornnic", FantasyNameGenerator.Fuse("Thorn", "nic", dedupe: true));
    }

    [Fact]
    public void CompoundTables_FeminineNamesTakeFeminineEndings_AndBoundStemsNeverStandAlone()
    {
        var store = FantasyNameGenerator.Shared;
        foreach (var culture in new[] { Cultures.EasternEuropean, Cultures.WesternEuropean, Cultures.English, Cultures.FantasyCommon })
        {
            var table = store.Get(culture);
            var bound = table.GivenStems.Where(m => m.Tags.Contains("bound")).Select(m => m.Form).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var mascEndings = table.SecondStems.Where(m => m.Tags.Contains("masc")).Select(m => m.Form).ToList();
            Assert.NotEmpty(bound);
            Assert.Contains(table.SecondStems, m => m.Tags.Contains("fem"));
            for (int seed = 1; seed <= 150; seed++)
            {
                foreach (var feminine in new[] { false, true })
                {
                    var given = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed, feminine: feminine).Parts[0];
                    Assert.DoesNotContain(given.Form, bound);
                    if (feminine)
                        Assert.DoesNotContain(mascEndings, e => given.Form.EndsWith(e, StringComparison.OrdinalIgnoreCase)
                            && !table.SecondStems.Any(f => f.Tags.Contains("fem") && given.Form.EndsWith(f.Form, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
    }

    [Fact]
    public void EveryShippedStem_IsReachable()
    {
        // A given or second stem that repeats its own table's fused marker in both genders
        // ("Spark" next to "-spark"/"-sparkess") can never be picked: dead data.
        foreach (var (key, table) in FantasyNameGenerator.Shared.Tables)
            foreach (var m in table.GivenStems.Concat(table.SecondStems))
                Assert.False(FantasyNameGenerator.BlockedByMarker(table, m, false) && FantasyNameGenerator.BlockedByMarker(table, m, true),
                    $"{key}: '{m.Form}' repeats the patronymic marker and is never used.");
    }

    [Fact]
    public void Glosses_ReadAsEnglish()
    {
        foreach (var s in FantasyNameGenerator.Styles())
            for (int seed = 1; seed <= 60; seed++)
                foreach (var feminine in new[] { false, true })
                {
                    var g = FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, feminine: feminine).Gloss;
                    Assert.DoesNotContain(" of of ", g);
                    Assert.DoesNotContain(", of the", g);     // "eagle, of the house of" -> "of the house of eagle"
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
        // A river is never a "-hold" and a mountain never a "-water": every geography word
        // must carry the kind's tag. Shipped tables cover every kind, so the related-kind and
        // any-noun fallbacks never fire for them.
        var store = FantasyNameGenerator.Shared;
        var common = store.Get(Cultures.FantasyCommon);
        foreach (var s in FantasyNameGenerator.Styles())
        {
            var table = store.Get(s.TableKey);
            var nouns = table.PlaceFallback ? table.Nouns.Concat(common.Nouns).ToList() : table.Nouns;
            foreach (var kind in Kinds)
            {
                var fit = nouns.Where(n => KindTags(kind).Any(k => n.Tags.Contains(k)))
                    .Select(n => n.Form.ToLowerInvariant()).ToHashSet();
                Assert.True(fit.Count > 0, $"{s.Label}: no noun for '{kind}'.");
                for (int seed = 1; seed <= 30; seed++)
                {
                    var p = FantasyNameGenerator.GeneratePlace(kind, new PlaceContext(), s.Race, s.Culture, seed);
                    var geo = p.Parts.Single(x => x.Role == NamePartRoles.Geography).Form.ToLowerInvariant();
                    Assert.True(fit.Contains(geo), $"{s.Label}: '{p.Text}' uses '{geo}' for a {kind}.");
                }
            }
        }
    }

    /// <summary>Mirrors the generator: a city may use town words, a hold town words, a camp hill words.</summary>
    private static string[] KindTags(string kind) => kind switch
    {
        "city" => new[] { "city", "town" },
        "hold" => new[] { "hold", "town" },
        "camp" => new[] { "camp", "hill" },
        _ => new[] { kind },
    };

    [Fact]
    public void PlaceNames_HaveVarietyForEveryKind()
    {
        // With no context a world still needs many distinct names per kind; a table that can
        // only say "Novgrad" or "Stargrad" for every town is not usable.
        foreach (var s in FantasyNameGenerator.Styles())
            foreach (var kind in Kinds)
            {
                int distinct = Enumerable.Range(1, 80)
                    .Select(seed => FantasyNameGenerator.GeneratePlace(kind, new PlaceContext(), s.Race, s.Culture, seed).Text)
                    .Distinct().Count();
                Assert.True(distinct >= 10, $"{s.Label}: only {distinct} distinct {kind} names in 80 rolls.");
            }
    }

    [Fact]
    public void PersonNames_HaveVariety()
    {
        foreach (var s in FantasyNameGenerator.Styles())
            foreach (var feminine in new[] { false, true })
            {
                int distinct = Enumerable.Range(1, 200)
                    .Select(seed => FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed, feminine: feminine).Text)
                    .Distinct().Count();
                Assert.True(distinct >= 150, $"{s.Label} (feminine={feminine}): only {distinct} distinct names in 200 rolls.");
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
