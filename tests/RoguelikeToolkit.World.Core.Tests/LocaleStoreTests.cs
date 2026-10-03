using RoguelikeToolkit.World.Core.Localization;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class LocaleStoreTests
{
    [Fact]
    public void AllShippedLocales_Load_WithMatchingKeySets()
    {
        // Load itself validates that every locale carries exactly the English keys.
        foreach (var (language, _) in LocaleStore.AvailableLanguages())
        {
            var locale = LocaleStore.Load(language);
            Assert.False(string.IsNullOrWhiteSpace(locale.Get("Title")));
            Assert.False(string.IsNullOrWhiteSpace(locale.Get("Generate")));
        }
    }

    [Fact]
    public void AvailableLanguages_ListsEnRuHe()
    {
        var languages = LocaleStore.AvailableLanguages().Select(l => l.Language).ToHashSet();
        Assert.Contains("en", languages);
        Assert.Contains("ru", languages);
        Assert.Contains("he", languages);
    }

    [Fact]
    public void Hebrew_IsRightToLeft_OthersAreNot()
    {
        Assert.True(LocaleStore.Load("he").RightToLeft);
        Assert.False(LocaleStore.Load("en").RightToLeft);
        Assert.False(LocaleStore.Load("ru").RightToLeft);
    }

    [Fact]
    public void UnknownLanguage_Throws_WithAvailable()
    {
        var ex = Assert.Throws<LocaleDataException>(() => LocaleStore.Load("xx"));
        Assert.Contains("en", ex.Message);
    }

    [Fact]
    public void Get_MarksUnknownKeys()
    {
        Assert.Equal("!NoSuchKey!", LocaleStore.Load("en").Get("NoSuchKey"));
    }
}
