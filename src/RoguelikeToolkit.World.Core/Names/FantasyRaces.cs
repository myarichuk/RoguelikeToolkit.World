namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Fantasy ancestry keys. DarkElf is a generic folklore archetype, not Drow.
/// Plain strings on purpose: new folk (e.g. "troll") can be added with a
/// YAML-only drop, no code changes. See <see cref="Cultures"/> the same way.
/// </summary>
public static class FantasyRaces
{
    public const string Human = "human";
    public const string Dwarf = "dwarf";
    public const string Halfling = "halfling";
    public const string Gnome = "gnome";
    public const string ElfHigh = "elf_high";
    public const string ElfWood = "elf_wood";
    public const string ElfDark = "elf_dark";
    public const string Orc = "orc";
    public const string HalfElf = "half_elf";
    public const string HalfOrc = "half_orc";
    public const string Lizardfolk = "lizardfolk";
    public const string Dragonfolk = "dragonfolk";
    public const string Catfolk = "catfolk";
    public const string Dogfolk = "dogfolk";
    public const string Demon = "demon";
    public const string Angel = "angel";
    public const string ElementalFire = "elemental_fire";
    public const string ElementalWater = "elemental_water";
    public const string ElementalEarth = "elemental_earth";
    public const string ElementalAir = "elemental_air";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Human, Dwarf, Halfling, Gnome, ElfHigh, ElfWood, ElfDark, Orc,
        HalfElf, HalfOrc, Lizardfolk, Dragonfolk, Catfolk, Dogfolk,
        Demon, Angel, ElementalFire, ElementalWater, ElementalEarth, ElementalAir,
    };
}
