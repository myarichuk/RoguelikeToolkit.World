namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// One YAML name table, either a folk (dwarven, orcish) or a human culture: the morpheme lists plus the chances and rules for assembling person and place names from them.
/// Loaded and validated by <see cref="NameDataStore"/>.
/// </summary>
[Preserve] // populated by YamlDotNet through reflection
public sealed class CultureData
{
    /// <summary>Table key, which must match the file name (case-insensitive) and is how races, cultures and aliases resolve to this table.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-readable label for pickers ("Human · Hebrew"). Blank falls back to a title-cased Key.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Chance (0-100) that a given name gets a second stem. Tables of whole real names set 0.</summary>
    public int CompoundChance { get; set; } = 65;
    /// <summary>Chance (0-100) of a clan/warband slot (when Clans exist). -1 = default (45 human, 80 otherwise).</summary>
    public int ClanChance { get; set; } = -1;
    /// <summary>Chance (0-100) of an epithet slot (when Epithets exist).</summary>
    public int EpithetChance { get; set; } = 0;
    /// <summary>Chance (0-100) of a family/house name once the clan slot did not fire.</summary>
    public int FamilyChance { get; set; } = 100;
    /// <summary>Family name comes first in the written name (Chinese, Japanese).</summary>
    public bool FamilyFirst { get; set; } = false;
    /// <summary>"DescriptorFirst" (Clear+ford) or "NounFirst" (Kfar Zahav, Caer+goch).</summary>
    public string PlaceOrder { get; set; } = "DescriptorFirst";
    /// <summary>"Fuse" (one word: Clearford), "Space" (two words) or "Hyphen".</summary>
    public string PlaceJoin { get; set; } = "Fuse";
    /// <summary>
    /// Initial mutation of a descriptor that follows a feminine (<c>fem</c>-tagged) place noun.
    /// "Soft" is Welsh soft mutation: Pont + du is "Pontddu", Moel + mawr is "Moelfawr",
    /// Caer + gwyn (feminine gwen) is "Caerwen"; a masculine noun leaves it alone ("Llyndu").
    /// Blank (default) mutates nothing.
    /// </summary>
    public string PlaceMutation { get; set; } = string.Empty;
    /// <summary>
    /// When true (default) the shared fantasy_common place vocabulary tops up this
    /// table's descriptors/nouns. Language-faithful tables set false so a Hebrew
    /// or Japanese place never comes out as "Clearwood".
    /// </summary>
    public bool PlaceFallback { get; set; } = true;
    /// <summary>Extra race names that resolve to this table (YAML-only extension).</summary>
    public List<string> Aliases { get; set; } = new();
    /// <summary>Whether person names get a patronymic/matronymic slot. Orcs, gnomes etc. set false.</summary>
    public bool Patronymic { get; set; } = true;
    /// <summary>
    /// When true the patronymic marker goes before the parent name ("mac Bran",
    /// "ben David", "ibn Karim") instead of fusing after it ("Bran"+"son").
    /// For prefix particles like Welsh map/ferch, Irish mac/nic, Arabic ibn/bint,
    /// Hebrew ben/bat.
    /// </summary>
    public bool PatronymicPrefix { get; set; } = false;
    /// <summary>
    /// When true, family affixes fuse onto the given stem as a suffix
    /// ("Ald"+"bury", "Mil"+"ov"). When false (default) they stand alone as
    /// their own name part ("Starhaven", "Li", "al-Sahra"). Suffix tables
    /// (dwarven, fantasy_common, human_eastern, human_western) set this true.
    /// </summary>
    public bool FamilySuffix { get; set; } = false;
    /// <summary>
    /// When true, the clan/family slot replaces the patronymic instead of stacking
    /// with it: a name gets "David ben Yosef" or "David Cohen", never
    /// "David ben Yosef Cohen". For cultures where both are idiomatic together
    /// (Russian "Ivan Petrovich Sokolov", Arabic ism + nasab + nisba) leave false.
    /// Hebrew sets this true.
    /// </summary>
    public bool FamilyReplacesPatronymic { get; set; } = false;
    /// <summary>Whose line the name follows when the caller does not choose: <c>Patrilineal</c>, <c>Matrilineal</c> or <c>Bilineal</c> (validated at load, case-insensitive). See <see cref="Lineages"/>.</summary>
    public string LineageDefault { get; set; } = "Patrilineal";
    /// <summary>Free-text note on where this table's material comes from; copied into <see cref="NameProvenance.Note"/> of every name it generates.</summary>
    public string Provenance { get; set; } = "original";
    /// <summary>Given-name stems; required (at least one). Also supply the parent name of a patronymic and the carrier stem of a fused family suffix, so they set the given-name vocabulary of the whole table.</summary>
    public List<MorphemeYaml> GivenStems { get; set; } = new();
    /// <summary>Optional endings fused onto a given stem to make a compound given name; taken with probability <see cref="CompoundChance"/> (always for stems tagged <c>bound</c>).</summary>
    public List<MorphemeYaml> SecondStems { get; set; } = new();
    /// <summary>Family or house names, or suffixes that fuse onto a stem when <see cref="FamilySuffix"/> is true; taken with probability <see cref="FamilyChance"/> when no clan was chosen.</summary>
    public List<MorphemeYaml> FamilyAffixes { get; set; } = new();
    /// <summary>Clan, warband or house names; taken with probability <see cref="ClanChance"/>. They become the <see cref="NamePartRoles.Family"/> part for humans and <see cref="NamePartRoles.Clan"/> for other folk.</summary>
    public List<MorphemeYaml> Clans { get; set; } = new();
    /// <summary>Epithets such as deed-names, appended with probability <see cref="EpithetChance"/> when the name has fewer than three parts.</summary>
    public List<MorphemeYaml> Epithets { get; set; } = new();
    /// <summary>Place-name descriptors (the "Clear" of Clearford); tagged ones are weighted by how well the tags fit the place kind and <see cref="PlaceContext"/>.</summary>
    public List<MorphemeYaml> Descriptors { get; set; } = new();
    /// <summary>Place-name nouns (the "ford" of Clearford), tagged by the <see cref="PlaceKinds"/> they suit; an empty list falls back to the shared <c>fantasy_common</c> nouns.</summary>
    public List<MorphemeYaml> Nouns { get; set; } = new();
    /// <summary>Patronymic marker for a son, fused after the parent name or written before it per <see cref="PatronymicPrefix"/>; must be non-blank when <see cref="Patronymic"/> is true.</summary>
    public string PatronymicSon { get; set; } = "son";
    /// <summary>Patronymic marker for a daughter; must be non-blank when <see cref="Patronymic"/> is true.</summary>
    public string PatronymicDaughter { get; set; } = "dottir";
    /// <summary>Latin transliterations of the patronymic markers (Russian ovich/ovna); blank falls back to the markers above.</summary>
    public string PatronymicSonLatin { get; set; } = string.Empty;
    /// <summary>Latin transliteration of <see cref="PatronymicDaughter"/>; blank falls back to the marker itself.</summary>
    public string PatronymicDaughterLatin { get; set; } = string.Empty;
    /// <summary>Forbidden words (case-insensitive substrings). A generated name containing one, or one from <c>fantasy_common</c>, is rerolled; in the Hebrew and Cyrillic scripts the transliterated ban applies too.</summary>
    public List<string> Banned { get; set; } = new();
}
