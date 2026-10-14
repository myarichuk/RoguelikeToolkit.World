namespace RoguelikeToolkit.World.Core.Names;

/// <summary>
/// Fantasy ancestry keys. DarkElf is a generic folklore archetype, not Drow.
/// Plain strings on purpose: new folk (e.g. "troll") can be added with a
/// YAML-only drop, no code changes. See <see cref="Cultures"/> the same way.
/// <remarks>Deprecated as the folk registry: the generated folk index
/// (<see cref="RoguelikeToolkit.World.Core.Peoples.PeoplesStore.FolkKeys"/>) is the
/// source of truth for which folk exist. This class stays as a shim so existing
/// race keys keep compiling and producing identical names.</remarks>
/// </summary>
public static class FantasyRaces
{
    /// <summary>Humans, whose names come from the chosen culture rather than a race table.</summary>
    public const string Human = "human";
    /// <summary>Dwarves (table <c>dwarven</c>).</summary>
    public const string Dwarf = "dwarf";
    /// <summary>Halflings (table <c>smallfolk</c>).</summary>
    public const string Halfling = "halfling";
    /// <summary>Gnomes (table <c>gnomish</c>).</summary>
    public const string Gnome = "gnome";
    /// <summary>High elves (table <c>elf_high</c>).</summary>
    public const string ElfHigh = "elf_high";
    /// <summary>Wood elves (table <c>elf_wood</c>).</summary>
    public const string ElfWood = "elf_wood";
    /// <summary>Dark elves, a generic folklore archetype rather than Drow (table <c>elf_dark</c>).</summary>
    public const string ElfDark = "elf_dark";
    /// <summary>Orcs (table <c>orcish</c>).</summary>
    public const string Orc = "orc";
    /// <summary>Half-elves: elven given name plus a borrowed human family name (table <c>elf_high</c>).</summary>
    public const string HalfElf = "half_elf";
    /// <summary>Half-orcs: orcish given name plus either an orcish epithet or a borrowed human family name (table <c>orcish</c>).</summary>
    public const string HalfOrc = "half_orc";
    /// <summary>Lizardfolk (table <c>lizardfolk</c>).</summary>
    public const string Lizardfolk = "lizardfolk";
    /// <summary>Dragonfolk (table <c>dragonfolk</c>).</summary>
    public const string Dragonfolk = "dragonfolk";
    /// <summary>Cat-people (table <c>catfolk</c>).</summary>
    public const string Catfolk = "catfolk";
    /// <summary>Dog-people (table <c>dogfolk</c>).</summary>
    public const string Dogfolk = "dogfolk";
    /// <summary>Demons (table <c>demonic</c>).</summary>
    public const string Demon = "demon";
    /// <summary>Angels (table <c>angelic</c>).</summary>
    public const string Angel = "angel";
    /// <summary>Fire elementals (table <c>elemental_fire</c>).</summary>
    public const string ElementalFire = "elemental_fire";
    /// <summary>Water elementals (table <c>elemental_water</c>).</summary>
    public const string ElementalWater = "elemental_water";
    /// <summary>Earth elementals (table <c>elemental_earth</c>).</summary>
    public const string ElementalEarth = "elemental_earth";
    /// <summary>Air elementals (table <c>elemental_air</c>).</summary>
    public const string ElementalAir = "elemental_air";

    /// <summary>Every shipped race key, in declaration order; YAML-added and runtime-registered races are not included (see <see cref="FantasyNameGenerator.AvailableRaces"/>). Kept as a shim; new code should read the generated folk index instead.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Human, Dwarf, Halfling, Gnome, ElfHigh, ElfWood, ElfDark, Orc,
        HalfElf, HalfOrc, Lizardfolk, Dragonfolk, Catfolk, Dogfolk,
        Demon, Angel, ElementalFire, ElementalWater, ElementalEarth, ElementalAir,
    };
}
