namespace RoguelikeToolkit.World.Core.Names;

[Preserve] // populated by YamlDotNet through reflection
public sealed class CultureData
{
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
    public string LineageDefault { get; set; } = "Patrilineal";
    public string Provenance { get; set; } = "original";
    public List<MorphemeYaml> GivenStems { get; set; } = new();
    public List<MorphemeYaml> SecondStems { get; set; } = new();
    public List<MorphemeYaml> FamilyAffixes { get; set; } = new();
    public List<MorphemeYaml> Clans { get; set; } = new();
    public List<MorphemeYaml> Epithets { get; set; } = new();
    public List<MorphemeYaml> Descriptors { get; set; } = new();
    public List<MorphemeYaml> Nouns { get; set; } = new();
    public string PatronymicSon { get; set; } = "son";
    public string PatronymicDaughter { get; set; } = "dottir";
    /// <summary>Latin transliterations of the patronymic markers (Russian ovich/ovna); blank falls back to the markers above.</summary>
    public string PatronymicSonLatin { get; set; } = string.Empty;
    public string PatronymicDaughterLatin { get; set; } = string.Empty;
    public List<string> Banned { get; set; } = new();
}
