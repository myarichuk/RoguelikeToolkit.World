namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// Uses that overlap with a <see cref="LandUseInfo.Use"/> and with each other: a forest is both hunted and
/// logged, a pasture is also a nomad's range. Territory, not tenure, so they are bit flags rather than one
/// id. The top three bits are free for plugins.
/// </summary>
[Flags]
public enum LandActivity : byte
{
    None = 0,
    /// <summary>Herds are driven across it: a nomadic or seasonal range, as against fenced <see cref="LandUseId.Pasture"/>.</summary>
    Grazing = 1,
    Hunting = 2,
    /// <summary>Gathering of wild fruit, roots, honey, fuel.</summary>
    Foraging = 4,
    Logging = 8,
    /// <summary>Fishing grounds: sea, lake or river tiles.</summary>
    Fishing = 16,
}
