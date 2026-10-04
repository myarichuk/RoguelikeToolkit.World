using RoguelikeToolkit.World.Core.Names;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class TransliteratorTests
{
    [Fact]
    public void Hebrew_Vectors()
    {
        Assert.Equal("בן", Transliterator.ToHebrew("Ben"));
        Assert.Equal("סילאון", Transliterator.ToHebrew("Silawen"));
        Assert.Equal("תרוש", Transliterator.ToHebrew("Thrush"));
        Assert.Equal("סין", Transliterator.ToHebrew("Cian"));
        Assert.Equal("קרג", Transliterator.ToHebrew("Crag"));
        Assert.Equal("האול", Transliterator.ToHebrew("Howl"));
        Assert.Equal("ברן", Transliterator.ToHebrew("Bran"));
        Assert.Equal("איגל", Transliterator.ToHebrew("Eagle"));
        Assert.Equal("דאירדר", Transliterator.ToHebrew("Deirdre"));
        Assert.Equal("ירוסלב", Transliterator.ToHebrew("Yaroslav"));
    }

    [Fact]
    public void Cyrillic_Vectors()
    {
        // Capitalization follows the source word.
        Assert.Equal("Бен", Transliterator.ToCyrillic("Ben"));
        Assert.Equal("Владимир", Transliterator.ToCyrillic("Vladimir"));
        Assert.Equal("Труш", Transliterator.ToCyrillic("Thrush"));
        Assert.Equal("Сиан", Transliterator.ToCyrillic("Cian"));
        Assert.Equal("Бран", Transliterator.ToCyrillic("Bran"));
        Assert.Equal("Игле", Transliterator.ToCyrillic("Eagle"));
        Assert.Equal("Джон", Transliterator.ToCyrillic("John"));
        Assert.Equal("Ярослав", Transliterator.ToCyrillic("Yaroslav"));
        Assert.Equal("бран", Transliterator.ToCyrillic("bran"));
    }

    [Fact]
    public void TextAlreadyInTargetScript_PassesThrough()
    {
        Assert.Equal("מתת", Transliterator.Transliterate("מתת", NameScript.Hebrew));
        Assert.Equal("Владимир", Transliterator.Transliterate("Владимир", NameScript.Cyrillic));
    }

    [Fact]
    public void Render_PrefersAuthoredForms()
    {
        var ru = new MorphemeYaml { Form = "Владимир", Latin = "Vladimir", Gloss = "ruler of peace" };
        Assert.Equal("Владимир", Transliterator.Render(ru, NameScript.Native));
        Assert.Equal("Vladimir", Transliterator.Render(ru, NameScript.Latin));
        Assert.Equal("Владимир", Transliterator.Render(ru, NameScript.Cyrillic));
        Assert.Equal("ולדימיר", Transliterator.Render(ru, NameScript.Hebrew));
        var elf = new MorphemeYaml { Form = "Starhaven", Gloss = "house" };
        Assert.Equal("Стархавен", Transliterator.Render(elf, NameScript.Cyrillic));
        Assert.Equal("סטרהון", Transliterator.Render(elf, NameScript.Hebrew));
    }

    [Fact]
    public void FantasyNames_RenderInHebrewAndCyrillic()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var native = FantasyNameGenerator.GeneratePerson(FantasyRaces.ElfHigh, Cultures.FantasyCommon, seed);
            var hebrew = FantasyNameGenerator.GeneratePerson(FantasyRaces.ElfHigh, Cultures.FantasyCommon, seed, script: NameScript.Hebrew);
            var cyrillic = FantasyNameGenerator.GeneratePerson(FantasyRaces.ElfHigh, Cultures.FantasyCommon, seed, script: NameScript.Cyrillic);
            // Same picks, three renderings; the underlying etymology glosses
            // are script-independent (Gloss itself embeds the rendered forms).
            Assert.NotEqual(native.Text, hebrew.Text);
            Assert.NotEqual(native.Text, cyrillic.Text);
            Assert.Equal(native.Parts.Select(p => p.Gloss), hebrew.Parts.Select(p => p.Gloss));
            Assert.Equal(native.Parts.Select(p => p.Gloss), cyrillic.Parts.Select(p => p.Gloss));
            Assert.Equal(native.Parts.Select(p => p.Role), hebrew.Parts.Select(p => p.Role));
            Assert.Contains(hebrew.Text, c => c is >= '\u0590' and <= '\u05FF');
            Assert.Contains(cyrillic.Text, c => c is >= '\u0400' and <= '\u04FF');
            var hebrewAgain = FantasyNameGenerator.GeneratePerson(FantasyRaces.ElfHigh, Cultures.FantasyCommon, seed, script: NameScript.Hebrew);
            Assert.Equal(hebrew.Text, hebrewAgain.Text);
        }
    }

    [Fact]
    public void SameTableBan_HoldsInTransliteratedScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "selfban.yaml"),
                "Key: selfban\nLineageDefault: Patrilineal\nProvenance: test\nPatronymic: false\nPatronymicSon: son\nPatronymicDaughter: dottir\n" +
                "GivenStems:\n  - {Form: Zqx, Gloss: banned here}\n  - {Form: Ok, Gloss: fine}\n" +
                "SecondStems: []\nFamilyAffixes: []\nClans: []\nEpithets: []\nDescriptors: []\nNouns: []\nBanned: [zqx]\n");
            foreach (int seed in Enumerable.Range(1, 6))
            {
                var hebrew = FantasyNameGenerator.GeneratePerson("selfban", Cultures.FantasyCommon, seed, overrideDir: dir, script: NameScript.Hebrew);
                Assert.Equal(Transliterator.ToHebrew("Ok"), hebrew.Text);
                var cyrillic = FantasyNameGenerator.GeneratePerson("selfban", Cultures.FantasyCommon, seed, overrideDir: dir, script: NameScript.Cyrillic);
                Assert.Equal("Ок", cyrillic.Text);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
