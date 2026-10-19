namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// An author's decision about one founding polity, in <see cref="HistoryContext.Founders"/> order: founder <c>i</c> becomes
/// polity <c>i</c>. Every field is optional; what is left out is drawn as usual (a folk and culture from
/// <see cref="HistoryContext.Peoples"/>, a form from the culture's <c>FormAffinity</c> and the land, a capital from the
/// site scoring). Pins are not persisted on their own: they only shape the founding, and the polities they produce are saved.
/// </summary>
public sealed class HistoryFounder
{
    /// <summary>Founding folk key (a <c>FolkDef</c> key), or blank to draw one from the roster.</summary>
    public string FolkKey { get; init; } = string.Empty;

    /// <summary>Founding culture key, or blank for the roster's first culture of this folk (or a drawn pair when the folk is blank too).</summary>
    public string CultureKey { get; init; } = string.Empty;

    /// <summary>Pinned government form, or <c>null</c> to draw it from the culture's affinity and the land.</summary>
    public PolityForm? Form { get; init; }

    /// <summary>Latitude of the wanted capital in degrees (-90..90), or <c>null</c> for a scored site; needs <see cref="Longitude"/>.</summary>
    public double? Latitude { get; init; }

    /// <summary>Longitude of the wanted capital in degrees (-180..180), or <c>null</c> for a scored site; needs <see cref="Latitude"/>.</summary>
    public double? Longitude { get; init; }

    /// <summary>True when both coordinates are set.</summary>
    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;
}
