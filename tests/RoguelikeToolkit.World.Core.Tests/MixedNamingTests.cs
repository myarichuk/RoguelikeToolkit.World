using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Lineage;
using RoguelikeToolkit.World.Core.Names;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class MixedNamingTests
{
    private const string DomFolkTable = """
        Key: domfolk
        LineageDefault: Patrilineal
        Provenance: test
        Patronymic: false
        PatronymicSon: son
        PatronymicDaughter: dottir
        GivenStems:
          - {Form: DomA, Gloss: first of dom}
        SecondStems: []
        FamilyAffixes:
          - {Form: DomFam, Gloss: house of dom}
        Clans: []
        Epithets: []
        Descriptors: []
        Nouns: []
        Banned: []
        """;

    private const string SecFolkTable = """
        Key: secfolk
        LineageDefault: Patrilineal
        Provenance: test
        Patronymic: false
        PatronymicSon: son
        PatronymicDaughter: dottir
        GivenStems:
          - {Form: SecA, Gloss: first of sec}
        SecondStems: []
        FamilyAffixes:
          - {Form: SecFam, Gloss: house of sec}
        Clans: []
        Epithets: []
        Descriptors: []
        Nouns: []
        Banned: []
        """;

    private const string FolkYaml = """
        Sapience: Full
        Embodiment: humanoid
        HabitatAffinity: temperate
        LifespanYears: 80
        MaturationYears: 16
        GrowthR: 0.05
        CompatGroup: mammal
        OpennessPrior: balanced
        ConscientiousnessPrior: balanced
        ExtraversionPrior: balanced
        AgreeablenessPrior: balanced
        NeuroticismPrior: balanced
        """;

    private const string BlendCulture = """
        Key: mingled
        DisplayName: Mingled
        NamingTable: fantasy_common
        Subsistence: agrarian
        LineageDefault: Patrilineal
        Xenophobia: 10
        HierarchyClaim: 10
        Collectivism: 10
        Honor: 10
        FaithForm: animism
        MixedNaming: Blend
        DominantShareThreshold: 0.75
        OpennessOffset: 0
        ConscientiousnessOffset: 0
        ExtraversionOffset: 0
        AgreeablenessOffset: 0
        NeuroticismOffset: 0
        """;

    private const string DominantCulture = """
        Key: domrule
        DisplayName: Dom Rule
        NamingTable: fantasy_common
        Subsistence: agrarian
        LineageDefault: Patrilineal
        Xenophobia: 10
        HierarchyClaim: 10
        Collectivism: 10
        Honor: 10
        FaithForm: animism
        MixedNaming: Dominant
        DominantShareThreshold: 0.75
        OpennessOffset: 0
        ConscientiousnessOffset: 0
        ExtraversionOffset: 0
        AgreeablenessOffset: 0
        NeuroticismOffset: 0
        """;

    private static string WriteNamesFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mixed-names-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "domfolk.yaml"), DomFolkTable);
        File.WriteAllText(Path.Combine(dir, "secfolk.yaml"), SecFolkTable);
        return dir;
    }

    private static string WritePeoplesFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mixed-peoples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Folks"));
        Directory.CreateDirectory(Path.Combine(dir, "Cultures"));
        File.WriteAllText(Path.Combine(dir, "Folks", "domfolk.yaml"), "Key: domfolk\nDisplayName: Dom\n" + FolkYaml);
        File.WriteAllText(Path.Combine(dir, "Folks", "secfolk.yaml"), "Key: secfolk\nDisplayName: Sec\n" + FolkYaml);
        File.WriteAllText(Path.Combine(dir, "Cultures", "mingled.yaml"), BlendCulture);
        File.WriteAllText(Path.Combine(dir, "Cultures", "domrule.yaml"), DominantCulture);
        return dir;
    }

    private static Ancestry HalfDomSec()
        => Ancestry.From(new AncestryShare("domfolk", 0.5), new AncestryShare("secfolk", 0.5));

    [Fact]
    public void HalfAndHalf_NamesWithoutHalfKey_GivenFromDominantFamilyFromSecond()
    {
        var namesDir = WriteNamesFixture();
        var peoplesDir = WritePeoplesFixture();
        try
        {
            var peoples = PeoplesStore.Load(peoplesDir);
            var name = FantasyNameGenerator.GenerateMixedPerson(
                HalfDomSec(), "mingled", peoples, seed: 1, overrideDir: namesDir);
            Assert.Equal("DomA SecFam", name.Text);
            Assert.Equal(new[] { NamePartRoles.Given, NamePartRoles.Clan },
                name.Parts.Select(p => p.Role).ToArray());
            Assert.Equal("domfolk+secfolk", name.Race);
            Assert.Equal("mingled", name.Culture);
            Assert.Equal(Lineages.Patrilineal, name.Lineage);
        }
        finally
        {
            Directory.Delete(namesDir, recursive: true);
            Directory.Delete(peoplesDir, recursive: true);
        }
    }

    [Fact]
    public void DominantShare_NamesAfterThatFolk()
    {
        var namesDir = WriteNamesFixture();
        var peoplesDir = WritePeoplesFixture();
        try
        {
            var peoples = PeoplesStore.Load(peoplesDir);
            var ancestry = Ancestry.From(new AncestryShare("domfolk", 0.75), new AncestryShare("secfolk", 0.25));
            var name = FantasyNameGenerator.GenerateMixedPerson(
                ancestry, "mingled", peoples, seed: 1, overrideDir: namesDir);
            Assert.Equal("DomA DomFam", name.Text);
        }
        finally
        {
            Directory.Delete(namesDir, recursive: true);
            Directory.Delete(peoplesDir, recursive: true);
        }
    }

    [Fact]
    public void DominantStrategy_RendersDominantEvenWhenMixed()
    {
        var namesDir = WriteNamesFixture();
        var peoplesDir = WritePeoplesFixture();
        try
        {
            var peoples = PeoplesStore.Load(peoplesDir);
            var name = FantasyNameGenerator.GenerateMixedPerson(
                HalfDomSec(), "domrule", peoples, seed: 1, overrideDir: namesDir);
            Assert.Equal("DomA DomFam", name.Text);
        }
        finally
        {
            Directory.Delete(namesDir, recursive: true);
            Directory.Delete(peoplesDir, recursive: true);
        }
    }

    [Fact]
    public void ShippedHalfAndHalf_MixesWithoutHalfKey()
    {
        var peoples = PeoplesStore.Load();
        var ancestry = Ancestry.From(new AncestryShare("human", 0.5), new AncestryShare("dwarf", 0.5));
        var name = FantasyNameGenerator.GenerateMixedPerson(ancestry, "human_common", peoples, seed: 4);
        Assert.Equal("human+dwarf", name.Race);
        Assert.DoesNotContain("half", name.Race, StringComparison.OrdinalIgnoreCase);
        Assert.True(name.Parts.Count >= 2);
        Assert.Contains(name.Parts, p => p.Role is NamePartRoles.Family or NamePartRoles.Clan);
        var again = FantasyNameGenerator.GenerateMixedPerson(ancestry, "human_common", peoples, seed: 4);
        Assert.Equal(name.Text, again.Text);
        Assert.Equal(name.Gloss, again.Gloss);
    }

    [Fact]
    public void MixedPerson_RejectsBadInput()
    {
        var peoples = PeoplesStore.Load();
        var ancestry = Ancestry.From(new AncestryShare("human", 1.0));
        Assert.Throws<ArgumentNullException>(() =>
            FantasyNameGenerator.GenerateMixedPerson(null!, "human_common", peoples, seed: 1));
        Assert.Throws<ArgumentNullException>(() =>
            FantasyNameGenerator.GenerateMixedPerson(ancestry, "human_common", null!, seed: 1));
        Assert.Throws<UnknownPeoplesKeyException>(() =>
            FantasyNameGenerator.GenerateMixedPerson(ancestry, "troll_clan", peoples, seed: 1));
        var troll = Ancestry.From(new AncestryShare("troll", 1.0));
        Assert.Throws<UnknownNameKeyException>(() =>
            FantasyNameGenerator.GenerateMixedPerson(troll, "human_common", peoples, seed: 1));
    }

    [Theory]
    [InlineData(FantasyRaces.HalfElf, Cultures.FantasyCommon, false, 1, "Analightfoot Ilmaion Stormford")]
    [InlineData(FantasyRaces.HalfElf, Cultures.FantasyCommon, false, 2, "Miriel Wanaion Aldley")]
    [InlineData(FantasyRaces.HalfElf, Cultures.FantasyCommon, true, 2, "Miriwen Wanaiel Aldley")]
    [InlineData(FantasyRaces.HalfElf, Cultures.EasternEuropean, false, 3, "Ala Naraion Zvonov")]
    [InlineData(FantasyRaces.HalfElf, Cultures.WesternEuropean, true, 8, "Oloeaglesee Tintaiel Arnhofer")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.FantasyCommon, false, 1, "Durg Bonemaul")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.FantasyCommon, false, 2, "Brakwolf Stormbury")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.FantasyCommon, false, 4, "Shrakmaw Thornford")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.EasternEuropean, false, 2, "Cragrage Mudtread")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.EasternEuropean, false, 1, "Bloodmaw Bogski")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.WesternEuropean, false, 1, "Rug Skullbreaker")]
    [InlineData(FantasyRaces.HalfOrc, Cultures.WesternEuropean, false, 4, "Urzgash Harder")]
    public void HalfBloods_KeepIdenticalOutput(string race, string culture, bool feminine, int seed, string expected)
    {
        var name = FantasyNameGenerator.GeneratePerson(race, culture, seed, feminine: feminine);
        Assert.Equal(expected, name.Text);
    }

    [Fact]
    public void HalfElf_BorrowsHumanFamilyLine()
    {
        foreach (int seed in Enumerable.Range(1, 6))
        {
            var name = FantasyNameGenerator.GeneratePerson(
                FantasyRaces.HalfElf, Cultures.FantasyCommon, seed);
            var family = Assert.Single(name.Parts, p => p.Role == NamePartRoles.Family);
            Assert.Contains("(human side)", family.Gloss, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HalfOrc_LineageSlotIsEpithetOrHumanFamily()
    {
        foreach (int seed in Enumerable.Range(1, 8))
        {
            var name = FantasyNameGenerator.GeneratePerson(
                FantasyRaces.HalfOrc, Cultures.FantasyCommon, seed);
            Assert.Equal(2, name.Parts.Count);
            var slot = name.Parts[1];
            Assert.True(slot.Role is NamePartRoles.Epithet or NamePartRoles.Family, $"{seed}: {slot.Role}");
            if (slot.Role == NamePartRoles.Family)
                Assert.Contains("(human side)", slot.Gloss, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ConquestCity_YieldsSuperstrateDescriptorSubstrateNoun()
    {
        var store = FantasyNameGenerator.Shared;
        var superDescs = store.Get(Cultures.FantasyCommon).Descriptors
            .Select(d => d.Form).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dwarfNouns = store.Get("dwarven").Nouns
            .Select(n => n.Form).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commonNouns = store.Get(Cultures.FantasyCommon).Nouns
            .Select(n => n.Form).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dwarfOnly = dwarfNouns.Where(n => !commonNouns.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool sawSubstrate = false;
        foreach (int seed in Enumerable.Range(1, 12))
        {
            var city = FantasyNameGenerator.GenerateHybridPlace(
                PlaceKinds.City, new PlaceContext(),
                FantasyRaces.Human, Cultures.FantasyCommon, FantasyRaces.Dwarf, Cultures.FantasyCommon, seed);
            var desc = Assert.Single(city.Parts, p => p.Role == NamePartRoles.Descriptor);
            var noun = Assert.Single(city.Parts, p => p.Role == NamePartRoles.Geography);
            Assert.Contains(desc.Form, superDescs);
            Assert.True(dwarfNouns.Contains(noun.Form) || commonNouns.Contains(noun.Form),
                $"{seed}: noun '{noun.Form}' is from neither table.");
            sawSubstrate |= dwarfOnly.Contains(noun.Form);
            Assert.Contains("fantasy_common over dwarven", city.Provenance.Note, StringComparison.Ordinal);
        }
        Assert.True(sawSubstrate, "The substrate voice never surfaced in 12 cities.");
    }

    [Fact]
    public void HybridPlace_IsDeterministic()
    {
        var a = FantasyNameGenerator.GenerateHybridPlace(
            PlaceKinds.City, new PlaceContext { NearWater = true },
            FantasyRaces.Human, Cultures.FantasyCommon, FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 5);
        var b = FantasyNameGenerator.GenerateHybridPlace(
            PlaceKinds.City, new PlaceContext { NearWater = true },
            FantasyRaces.Human, Cultures.FantasyCommon, FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 5);
        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Gloss, b.Gloss);
    }

    [Fact]
    public void SettledNomads_KeepClanNamesOneGenerationThenBlend()
    {
        var peoples = PeoplesStore.Load();
        var ethnos = new Ethnos("dwarf", "dwarven_clan", new[]
        {
            new CultureDelta(1, "dwarven_clan", CultureDelta.Subsistence, "artisan", "agrarian", "settle"),
            new CultureDelta(2, "dwarven_clan", CultureDelta.Language, "dwarven", "fantasy_common", "trade-tongue"),
        });
        Assert.Equal("artisan", ethnos.Resolve(peoples, 0).Subsistence);
        var settled = ethnos.Resolve(peoples, 1);
        Assert.Equal("agrarian", settled.Subsistence);
        Assert.Equal("dwarven", settled.NamingTable);

        // Generation one still voices through the clan table: clan names kept.
        var keeper = FantasyNameGenerator.GeneratePerson(FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 4);
        Assert.Equal("Mor Bralson Oathbraid", keeper.Text);
        Assert.Contains(keeper.Parts, p => p.Role == NamePartRoles.Clan);

        // Generation two blends: the settled tongue describes, the clan land nouns.
        var blended = ethnos.Resolve(peoples, 2);
        Assert.Equal("fantasy_common", blended.NamingTable);
        var town = FantasyNameGenerator.GenerateHybridPlace(
            PlaceKinds.Village, new PlaceContext(),
            FantasyRaces.Human, blended.NamingTable, FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 2);
        Assert.Equal("Westbar", town.Text);
        var store = FantasyNameGenerator.Shared;
        var superDescs = store.Get(blended.NamingTable).Descriptors
            .Select(d => d.Form).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dwarfNouns = store.Get("dwarven").Nouns
            .Select(n => n.Form).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(town.Parts.Single(p => p.Role == NamePartRoles.Descriptor).Form, superDescs);
        Assert.Contains(town.Parts.Single(p => p.Role == NamePartRoles.Geography).Form, dwarfNouns);
    }
}
