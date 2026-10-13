namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Human-culture flavor keys (each is a YAML table key), plus FantasyCommon.</summary>
public static class Cultures
{
    /// <summary>Shared fantasy vocabulary; also the default culture and the place-name top-up table for tables with <see cref="CultureData.PlaceFallback"/>.</summary>
    public const string FantasyCommon = "fantasy_common";
    /// <summary>Eastern European human names (suffix surnames such as -ov).</summary>
    public const string EasternEuropean = "human_eastern";
    /// <summary>Western European human names (suffix surnames such as -bury).</summary>
    public const string WesternEuropean = "human_western";
    /// <summary>Welsh human names (map/ferch patronymics, noun-first place names with soft mutation).</summary>
    public const string Welsh = "human_welsh";
    /// <summary>Irish (Gaeilge) human names (mac/nic patronymics, noun-first place names).</summary>
    public const string Irish = "human_irish";
    /// <summary>Arabic human names (ism, ibn/bint nasab, al- nisba).</summary>
    public const string Arabic = "human_arabic";
    /// <summary>Chinese human names (family name first).</summary>
    public const string Chinese = "human_chinese";
    /// <summary>Japanese human names (family name first).</summary>
    public const string Japanese = "human_japanese";
    /// <summary>Old-English-flavoured human names (dithematic roots, no patronymic, -ham/-ton places).</summary>
    public const string English = "human_english";
    /// <summary>Russian human names (given name, patronymic, gendered surname; native Cyrillic with Latin forms).</summary>
    public const string Russian = "human_russian";
    /// <summary>Hebrew human names (ben/bat nasab or family name, never both; native Hebrew with Latin forms).</summary>
    public const string Hebrew = "human_hebrew";

    /// <summary>Every shipped culture key, in declaration order; YAML-added cultures are not included (see <see cref="FantasyNameGenerator.AvailableCultures"/>).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        FantasyCommon, EasternEuropean, WesternEuropean, Welsh, Irish, Arabic, Chinese, Japanese,
        English, Russian, Hebrew,
    };
}
