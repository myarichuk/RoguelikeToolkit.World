using RoguelikeToolkit.World.Core.Names;
using System.Text.RegularExpressions;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class NameGeneratorTests
{
    [Fact]
    public void Person_IsDeterministic_ForSameSeed()
    {
        var a = FantasyNameGenerator.GeneratePerson(FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 42);
        var b = FantasyNameGenerator.GeneratePerson(FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 42);
        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Gloss, b.Gloss);
    }

    [Fact]
    public void Person_Varies_BySeed_AndRace()
    {
        var names = new HashSet<string>();
        foreach (var race in FantasyRaces.All)
            for (int seed = 1; seed <= 10; seed++)
                names.Add(FantasyNameGenerator.GeneratePerson(race, Cultures.FantasyCommon, seed).Text);
        Assert.True(names.Count >= 100, $"Expected wide variety, got {names.Count} distinct of 200.");
    }

    [Fact]
    public void InspiredBy_BiasesTowardRequestedThemes()
    {
        int hits = 0, confirmed = 0;
        const int n = 40;
        for (int seed = 1; seed <= n; seed++)
        {
            var name = FantasyNameGenerator.GeneratePerson(
                FantasyRaces.Orc, Cultures.FantasyCommon, seed, inspiredBy: new[] { "blood", "eagle" });
            var gloss = name.Gloss.ToLowerInvariant();
            if (gloss.Contains("blood") || gloss.Contains("eagle")) hits++;
            if (name.Inspirations.Count > 0) confirmed++;
        }
        Assert.True(hits >= n / 2, $"Inspired-by barely applied: {hits}/{n}.");
        Assert.True(confirmed >= n / 2, $"Inspirations metadata rarely confirmed: {confirmed}/{n}.");
    }

    [Fact]
    public void InspiredBy_UnknownTokens_FallBackGracefully()
    {
        var name = FantasyNameGenerator.GeneratePerson(
            FantasyRaces.Human, Cultures.WesternEuropean, seed: 7, inspiredBy: new[] { "zzz-no-such-theme" });
        Assert.False(string.IsNullOrWhiteSpace(name.Text));
        Assert.Empty(name.Inspirations);
        Assert.All(name.Parts, p => Assert.False(string.IsNullOrWhiteSpace(p.Gloss)));
    }

    [Fact]
    public void LineageSwitch_ChangesRole_ForSameSeed()
    {
        var patri = FantasyNameGenerator.GeneratePerson(
            FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 5, lineage: Lineages.Patrilineal);
        var matri = FantasyNameGenerator.GeneratePerson(
            FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 5, lineage: Lineages.Matrilineal);
        Assert.Contains(patri.Parts, p => p.Role == NamePartRoles.Patronymic);
        Assert.Contains(matri.Parts, p => p.Role == NamePartRoles.Matronymic);
    }

    [Fact]
    public void Place_ReflectsWaterContext()
    {
        int hits = 0;
        const int n = 40;
        var ctx = new PlaceContext { NearWater = true, Kind = PlaceKinds.Village };
        for (int seed = 1; seed <= n; seed++)
        {
            var gloss = FantasyNameGenerator.GeneratePlace(
                PlaceKinds.Village, ctx, FantasyRaces.Human, Cultures.FantasyCommon, seed).Gloss.ToLowerInvariant();
            if (gloss.Contains("water") || gloss.Contains("river") || gloss.Contains("stream")
                || gloss.Contains("clear") || gloss.Contains("calm") || gloss.Contains("fast") || gloss.Contains("crossing"))
                hits++;
        }
        Assert.True(hits >= n / 3, $"Water context rarely reflected: {hits}/{n}.");
    }

    [Fact]
    public void Names_NeverEmitBannedWords()
    {
        foreach (var race in FantasyRaces.All)
            for (int seed = 1; seed <= 15; seed++)
            {
                var person = FantasyNameGenerator.GeneratePerson(race, Cultures.FantasyCommon, seed);
                var place = FantasyNameGenerator.GeneratePlace(
                    PlaceKinds.Village, new PlaceContext(), race, Cultures.FantasyCommon, seed);
                Assert.DoesNotContain("drow", person.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("gimli", person.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("drow", place.Text, StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public void EveryPart_HasGloss()
    {
        foreach (var race in FantasyRaces.All)
            for (int seed = 1; seed <= 5; seed++)
            {
                var name = FantasyNameGenerator.GeneratePerson(race, Cultures.FantasyCommon, seed);
                Assert.NotEmpty(name.Parts);
                Assert.All(name.Parts, p =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(p.Form));
                    Assert.False(string.IsNullOrWhiteSpace(p.Gloss));
                });
            }
    }

    [Fact]
    public void UnknownRace_Throws_WithAvailableKeys_AndSuggestion()
    {
        var ex = Assert.Throws<UnknownNameKeyException>(() =>
            FantasyNameGenerator.GeneratePerson("dwrf", Cultures.FantasyCommon, seed: 1));
        Assert.Equal("dwrf", ex.Key);
        Assert.Contains("dwarf", ex.Available, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("dwarf", ex.Suggestion);
    }

    [Fact]
    public void UnknownCulture_Throws_WithAvailableKeys()
    {
        var ex = Assert.Throws<UnknownNameKeyException>(() =>
            FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, "elvish", seed: 1));
        Assert.Equal("culture", ex.Kind);
        Assert.NotEmpty(ex.Available);
    }

    [Fact]
    public void NewFolk_FromYamlOnlyDrop_NeedsNoCode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "troll.yaml"),
                "Key: troll\nLineageDefault: Patrilineal\nProvenance: test\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Grubb, Gloss: grubber}\n  - {Form: Snarl, Gloss: snarler}\n" +
                "SecondStems: []\nFamilyAffixes: []\nClans:\n  - {Form: Mossback, Gloss: mossy clan}\n" +
                "Epithets: []\nDescriptors: []\nNouns: []\nBanned: []\n");
            var store = NameDataStore.Load(dir);
            var name = FantasyNameGenerator.GeneratePerson(
                "troll", Cultures.FantasyCommon, seed: 3, overrideDir: dir);
            Assert.Contains(name.Parts, p => p.Form.Contains("Grubb") || p.Form.Contains("Snarl"));
            Assert.Contains("troll", FantasyNameGenerator.AvailableRaces(store));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OnDiskYaml_OverridesEmbedded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "dwarven.yaml"),
                "Key: dwarven\nLineageDefault: Patrilineal\nProvenance: test\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Teststone, Gloss: test stone}\nSecondStems: []\nFamilyAffixes: []\nClans: []\nEpithets: []\nDescriptors: []\nNouns: []\nBanned: []\n");
            var name = FantasyNameGenerator.GeneratePerson(
                FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 1, overrideDir: dir);
            Assert.Contains("Teststone", name.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BrokenYaml_Throws_NameDataException()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "dwarven.yaml"), "Key: [broken yaml {{{");
            Assert.Throws<NameDataException>(() => NameDataStore.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DuplicateKeys_Throw_NameDataException()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "a"));
        Directory.CreateDirectory(Path.Combine(dir, "b"));
        try
        {
            const string yaml = "Key: twinfake\nGivenStems: []\nSecondStems: []\n";
            File.WriteAllText(Path.Combine(dir, "a", "twinfake.yaml"), yaml);
            File.WriteAllText(Path.Combine(dir, "b", "twinfake.yaml"), yaml);
            var ex = Assert.Throws<NameDataException>(() => NameDataStore.Load(dir));
            Assert.Contains(ex.Problems, p => p.Contains("duplicate"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeyFilenameMismatch_Throws_NameDataException()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "wrongname.yaml"), "Key: rightkey\nGivenStems: []\n");
            var ex = Assert.Throws<NameDataException>(() => NameDataStore.Load(dir));
            Assert.Contains(ex.Problems, p => p.Contains("does not match filename"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrefixPatronymics_LeadWithMarker()
    {
        // Welsh map/ferch, Irish mac/nic, Arabic ibn/bint, Hebrew ben/bat are
        // words before the parent name, never fused suffixes ("Branmap").
        foreach (int seed in Enumerable.Range(1, 10))
        {
            var welsh = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Welsh, seed);
            Assert.StartsWith("map ", welsh.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
            var welshFem = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Welsh, seed, feminine: true);
            Assert.StartsWith("ferch ", welshFem.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
            var irish = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Irish, seed);
            Assert.StartsWith("mac ", irish.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
            var irishFem = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Irish, seed, feminine: true);
            Assert.StartsWith("nic ", irishFem.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
            var arabic = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Arabic, seed);
            Assert.StartsWith("ibn ", arabic.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
            var hebrew = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Hebrew, seed);
            Assert.StartsWith("בן ", hebrew.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
            var hebrewLatin = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Hebrew, seed, script: NameScript.Latin);
            Assert.StartsWith("ben ", hebrewLatin.Parts.Single(p => p.Role == NamePartRoles.Patronymic).Form, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ArabicParticles_JoinWithSpace()
    {
        // "Karim al-Din", never "Karimal-Din".
        foreach (int seed in Enumerable.Range(1, 20))
        {
            var name = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Arabic, seed);
            Assert.DoesNotMatch(new Regex("[A-Za-z]al-"), name.Text);
        }
    }

    [Fact]
    public void FamilyHouses_StandAlone_InsteadOfFusing()
    {
        // "Underbough", never "MerUnderbough".
        var store = NameDataStore.Load();
        var table = store.Get("smallfolk");
        var houses = table.FamilyAffixes.Select(m => m.Form)
            .Concat(table.Clans.Select(m => m.Form)).ToHashSet(StringComparer.Ordinal);
        foreach (int seed in Enumerable.Range(1, 10))
        {
            var name = FantasyNameGenerator.GeneratePerson(FantasyRaces.Halfling, Cultures.FantasyCommon, seed);
            foreach (var part in name.Parts.Where(p => p.Role is NamePartRoles.Family or NamePartRoles.Clan))
                Assert.Contains(part.Form, houses);
        }
    }

    [Fact]
    public void RaceDispatch_IsCaseInsensitive()
    {
        foreach (var race in new[] { FantasyRaces.Dwarf, FantasyRaces.Orc, FantasyRaces.HalfElf, FantasyRaces.Dragonfolk })
        {
            var lower = FantasyNameGenerator.GeneratePerson(race, Cultures.FantasyCommon, seed: 5);
            var upper = FantasyNameGenerator.GeneratePerson(race.ToUpperInvariant(), Cultures.FantasyCommon, seed: 5);
            Assert.Equal(lower.Text, upper.Text);
            Assert.Equal(lower.Gloss, upper.Gloss);
        }
    }

    [Fact]
    public void CultureTypo_Throws_ForNonHumans()
    {
        var ex = Assert.Throws<UnknownNameKeyException>(() =>
            FantasyNameGenerator.GeneratePerson(FantasyRaces.Dwarf, "elvish", seed: 1));
        Assert.Equal("culture", ex.Kind);
    }

    [Fact]
    public void EmptyLineage_UsesTableDefault()
    {
        var cat = FantasyNameGenerator.GeneratePerson(FantasyRaces.Catfolk, Cultures.FantasyCommon, seed: 3, lineage: "");
        Assert.Equal(Lineages.Matrilineal, cat.Lineage);
        Assert.Contains(cat.Parts, p => p.Role == NamePartRoles.Matronymic);
        var dwarf = FantasyNameGenerator.GeneratePerson(FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 3, lineage: "  ");
        Assert.Equal(Lineages.Patrilineal, dwarf.Lineage);
    }

    [Fact]
    public void PlaceCompounds_HaveNoMidWordCapitals()
    {
        // "Clearford", never "ClearFord".
        var midCap = new Regex("[a-z][A-Z]");
        foreach (int seed in Enumerable.Range(1, 30))
        {
            var place = FantasyNameGenerator.GeneratePlace(
                PlaceKinds.Village, new PlaceContext(), FantasyRaces.Human, Cultures.FantasyCommon, seed);
            Assert.DoesNotMatch(midCap, place.Text);
        }
    }

    [Fact]
    public void NewCultures_GenerateInBothScripts()
    {
        Assert.Contains(Cultures.English, FantasyNameGenerator.AvailableCultures());
        Assert.Contains(Cultures.Russian, FantasyNameGenerator.AvailableCultures());
        Assert.Contains(Cultures.Hebrew, FantasyNameGenerator.AvailableCultures());
        foreach (var culture in new[] { Cultures.English, Cultures.Russian, Cultures.Hebrew })
        {
            foreach (int seed in Enumerable.Range(1, 10))
            {
                var native = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed);
                var again = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed);
                Assert.Equal(native.Text, again.Text);
                var latin = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed, script: NameScript.Latin);
                var latinAgain = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, culture, seed, script: NameScript.Latin);
                Assert.Equal(latin.Text, latinAgain.Text);
                Assert.NotEmpty(native.Parts);
                Assert.All(native.Parts, p => Assert.False(string.IsNullOrWhiteSpace(p.Gloss)));
            }
        }
        // English has no transliterations: both scripts render identically.
        Assert.Equal(
            FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.English, seed: 4).Text,
            FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.English, seed: 4, script: NameScript.Latin).Text);
        // Russian renders Cyrillic natively, ASCII in Latin script.
        var ruNative = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Russian, seed: 1);
        Assert.Contains(ruNative.Text, c => c > 127);
        var ruLatin = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Russian, seed: 1, script: NameScript.Latin);
        Assert.All(ruLatin.Text, c => Assert.True(c < 128));
        // Hebrew renders Hebrew natively, ASCII in Latin script.
        var heNative = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Hebrew, seed: 1);
        Assert.Contains(heNative.Text, c => c is >= '\u0590' and <= '\u05FF');
        var heLatin = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, Cultures.Hebrew, seed: 1, script: NameScript.Latin);
        Assert.All(heLatin.Text, c => Assert.True(c < 128));
    }

    [Fact]
    public void HalfOrc_UsesRequestedCulture()
    {
        // Eastern family markers carry (masc)/(fem); fantasy_common ones do not.
        bool sawEastern = false;
        foreach (int seed in Enumerable.Range(1, 20))
        {
            var name = FantasyNameGenerator.GeneratePerson(
                FantasyRaces.HalfOrc, Cultures.EasternEuropean, seed);
            sawEastern |= name.Parts.Any(p =>
                p.Gloss.Contains("(human side)", StringComparison.Ordinal) &&
                (p.Gloss.Contains("(masc)", StringComparison.Ordinal) || p.Gloss.Contains("(fem)", StringComparison.Ordinal)));
        }
        Assert.True(sawEastern, "Half-orc names never drew on the requested culture's family table.");
    }

    [Fact]
    public void BannedWords_AreScopedToTheirTable()
    {
        // "zqx" is banned by bantable only: cleantable may still emit it.
        // Seed 1 deterministically draws the Zqx stem for this fixture.
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string tail = "SecondStems: []\nFamilyAffixes: []\nClans: []\nEpithets: []\nDescriptors: []\nNouns: []\n";
            File.WriteAllText(Path.Combine(dir, "cleantable.yaml"),
                "Key: cleantable\nLineageDefault: Patrilineal\nProvenance: test\nPatronymic: false\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Zqx, Gloss: banned elsewhere}\n  - {Form: Ok, Gloss: fine}\n" + tail + "Banned: []\n");
            File.WriteAllText(Path.Combine(dir, "bantable.yaml"),
                "Key: bantable\nLineageDefault: Patrilineal\nProvenance: test\nPatronymic: false\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Zorb, Gloss: other}\n" + tail + "Banned: [zqx]\n");
            var name = FantasyNameGenerator.GeneratePerson("cleantable", Cultures.FantasyCommon, seed: 1, overrideDir: dir);
            Assert.Equal("Zqx", name.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SameTableBan_RetriesUntilClean()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "selfban.yaml"),
                "Key: selfban\nLineageDefault: Patrilineal\nProvenance: test\nPatronymic: false\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Zqx, Gloss: banned here}\n  - {Form: Ok, Gloss: fine}\n" +
                "SecondStems: []\nFamilyAffixes: []\nClans: []\nEpithets: []\nDescriptors: []\nNouns: []\nBanned: [zqx]\n");
            foreach (int seed in Enumerable.Range(1, 12))
            {
                var name = FantasyNameGenerator.GeneratePerson("selfban", Cultures.FantasyCommon, seed, overrideDir: dir);
                Assert.Equal("Ok", name.Text);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void YamlCultureDrop_ShowsUpInAvailableCultures()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "human_norse.yaml"),
                "Key: human_norse\nLineageDefault: Patrilineal\nProvenance: test\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Test, Gloss: test}\n" +
                "SecondStems: []\nFamilyAffixes: []\nClans: []\nEpithets: []\nDescriptors: []\nNouns: []\nBanned: []\n");
            var store = NameDataStore.Load(dir);
            Assert.Contains("human_norse", FantasyNameGenerator.AvailableCultures(store));
            var name = FantasyNameGenerator.GeneratePerson(FantasyRaces.Human, "human_norse", seed: 1, overrideDir: dir);
            Assert.Contains("Test", name.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BadTableContent_Throws_NameDataException()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "badlineage.yaml"),
                "Key: badlineage\nLineageDefault: Sideways\nProvenance: test\n" +
                "GivenStems:\n  - {Form: Test, Gloss: test}\n");
            File.WriteAllText(Path.Combine(dir, "nostems.yaml"),
                "Key: nostems\nLineageDefault: Patrilineal\nProvenance: test\nGivenStems: []\n");
            File.WriteAllText(Path.Combine(dir, "nogloss.yaml"),
                "Key: nogloss\nLineageDefault: Patrilineal\nProvenance: test\n" +
                "GivenStems:\n  - {Form: Test}\n");
            var ex = Assert.Throws<NameDataException>(() => NameDataStore.Load(dir));
            Assert.Contains(ex.Problems, p => p.Contains("badlineage") && p.Contains("LineageDefault"));
            Assert.Contains(ex.Problems, p => p.Contains("nostems") && p.Contains("GivenStems"));
            Assert.Contains(ex.Problems, p => p.Contains("nogloss") && p.Contains("Gloss"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
