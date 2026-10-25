namespace RoguelikeToolkit.World.Core.Peoples;

/// <summary>
/// Epithets persons earn from their personality and skills (cautious, reckless, cunning...). Data only; loaded from
/// <c>Peoples/Data/Traits/</c>, where <c>epithets.yaml</c> is the one the history uses.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class TraitTableDef
{
    /// <summary>Table key, matching the file name.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>The epithets, in the order ties are broken.</summary>
    public List<EpithetDef> Epithets { get; set; } = new();
}
