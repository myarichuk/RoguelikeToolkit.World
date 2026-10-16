namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One polity's faith: the culture's form plus deterministic powers drawn from the pressures the
/// polity lived under, plus myth slots naming the events the faith remembers. Data only; text
/// renders late through <c>Integration.IMythTextProvider</c> from these tags.
/// </summary>
public sealed class HistoryFaith
{
    /// <summary>Owning polity id.</summary>
    public int PolityId { get; init; }
    /// <summary>Faith form from the polity culture (animism, ancestor, polytheism, ...).</summary>
    public string Form { get; init; } = string.Empty;
    /// <summary>Two to seven power domains (war, harvest, sea, ...); see <see cref="FaithPostPass"/>.</summary>
    public List<string> Powers { get; } = new();
    /// <summary>Myth slots (<c>origin:{folk}</c>, <c>trauma:war:{n}</c>, <c>fall:self:{n}</c>, <c>fall:parent:{n}</c>, ...) the text layer renders.</summary>
    public List<string> MythSlots { get; } = new();
}
