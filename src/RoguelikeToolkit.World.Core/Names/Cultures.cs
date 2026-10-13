namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Human-culture flavor keys (each is a YAML table key), plus FantasyCommon.</summary>
public static class Cultures
{
    public const string FantasyCommon = "fantasy_common";
    public const string EasternEuropean = "human_eastern";
    public const string WesternEuropean = "human_western";
    public const string Welsh = "human_welsh";
    public const string Irish = "human_irish";
    public const string Arabic = "human_arabic";
    public const string Chinese = "human_chinese";
    public const string Japanese = "human_japanese";
    public const string English = "human_english";
    public const string Russian = "human_russian";
    public const string Hebrew = "human_hebrew";

    public static readonly IReadOnlyList<string> All = new[]
    {
        FantasyCommon, EasternEuropean, WesternEuropean, Welsh, Irish, Arabic, Chinese, Japanese,
        English, Russian, Hebrew,
    };
}
