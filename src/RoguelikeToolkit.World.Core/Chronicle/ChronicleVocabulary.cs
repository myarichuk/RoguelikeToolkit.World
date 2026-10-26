namespace RoguelikeToolkit.World.Core.Chronicle;

/// <summary>
/// Every fact <see cref="ChronicleFactBuilder"/> can write, for the lint: the slot names a template may use, the predicate
/// keys a rule may test, and for closed vocabularies the values a predicate may name (a typo such as <c>season=wintr</c>
/// would otherwise silently never match). Open vocabularies (beats, units, tactics, offices, causes...) are data and take any
/// value.
/// </summary>
internal static class ChronicleVocabulary
{
    /// <summary>Prefixes of a realm's facts: the actor (empty), the battle sides and the other party.</summary>
    public static readonly IReadOnlyList<string> RealmPrefixes = new[] { "", "att.", "def.", "win.", "lose.", "other." };

    /// <summary>Facts every realm prefix carries.</summary>
    public static readonly IReadOnlyList<string> RealmFacts = new[] { "realm", "culture", "people", "adj", "host", "cry" };

    /// <summary>Facts of a battle side beyond its realm.</summary>
    public static readonly IReadOnlyList<string> SideFacts = new[] { "unit", "tactic", "loss" };

    /// <summary>Person fact prefixes.</summary>
    public static readonly IReadOnlyList<string> PersonPrefixes = new[]
    {
        "who", "merchant", "merchant2", "outlaw", "privateer", "agent", "victim", "spymaster", "fallen", "att.general", "def.general", "win.general", "lose.general",
    };

    /// <summary>Facts every person prefix carries (the empty suffix is the name).</summary>
    public static readonly IReadOnlyList<string> PersonFacts = new[] { "", ".he", ".his", ".him", ".feminine", ".epithet", ".age", ".office" };

    /// <summary>Facts that stand alone.</summary>
    public static readonly IReadOnlyList<string> Singles = new[]
    {
        "turn", "year", "what", "tag", "form", "folk", "place",
        "battle", "kind", "season", "season.word", "terrain", "terrain.noun", "beat", "outcome", "winner", "margin",
        "settlement", "settlement.kind", "parent.settlement", "from.settlement", "months", "span", "end",
        "overlay", "treaty", "refused", "faith", "faith.word", "power", "power.word", "power.second", "origin", "origin.people", "trauma",
        "agenda", "agenda.word", "successors", "successor.count", "role", "why", "cause", "dir", "tier.from", "tier.to",
        "shock", "shock.word", "severity", "struck", "route.a", "route.b", "brigands", "source", "haven", "plot", "sponsor",
        "office", "who.parent", "who.reign", "tiles", "plugin", "fallen.realm",
    };

    /// <summary>The twelve shipped culture keys (the file names under <c>Peoples/Data/Cultures</c>).</summary>
    public static readonly IReadOnlyList<string> Cultures = new[]
    {
        "catfolk_prowl", "dogfolk_pack", "dragonfolk_aerie", "dwarven_clan", "elven_dark", "elven_high", "elven_wood",
        "gnomish_burrow", "halfling_hearth", "human_common", "lizardfolk_marsh", "orcish_horde",
    };

    /// <summary>The folk each shipped culture is founded by.</summary>
    public static readonly IReadOnlyDictionary<string, string> FolkOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["catfolk_prowl"] = "catfolk", ["dogfolk_pack"] = "dogfolk", ["dragonfolk_aerie"] = "dragonfolk", ["dwarven_clan"] = "dwarf",
        ["elven_dark"] = "elf_dark", ["elven_high"] = "elf_high", ["elven_wood"] = "elf_wood", ["gnomish_burrow"] = "gnome",
        ["halfling_hearth"] = "halfling", ["human_common"] = "human", ["lizardfolk_marsh"] = "lizardfolk", ["orcish_horde"] = "orc",
    };

    /// <summary>Closed predicate vocabularies: key → accepted values.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Closed = BuildClosed(); // after Cultures: static fields initialise in order

    private static HashSet<string>? _slots;

    /// <summary>Every slot name a fact can have.</summary>
    public static IReadOnlyCollection<string> Slots => _slots ??= BuildSlots();

    /// <summary>True when a fact named <paramref name="key"/> can exist.</summary>
    public static bool IsSlot(string key) => ((HashSet<string>)Slots).Contains(key);

    private static HashSet<string> BuildSlots()
    {
        var set = new HashSet<string>(Singles, StringComparer.Ordinal);
        foreach (var p in RealmPrefixes)
            foreach (var f in RealmFacts) set.Add(p + f);
        foreach (var p in new[] { "att.", "def.", "win.", "lose." })
            foreach (var f in SideFacts) set.Add(p + f);
        foreach (var p in PersonPrefixes)
            foreach (var f in PersonFacts) set.Add(p + f);
        return set;
    }

    private static Dictionary<string, string[]> BuildClosed()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["season"] = new[] { "spring", "summer", "autumn", "winter" },
            ["kind"] = new[] { "Field", "Ambush", "Siege", "Relief", "Naval" },
            ["outcome"] = new[] { "attacker_decisive", "attacker_victory", "attacker_pyrrhic", "stalemate", "defender_victory", "defender_decisive", "averted" },
            ["winner"] = new[] { "attacker", "defender", "none" },
            ["margin"] = new[] { "decisive", "victory", "pyrrhic", "stalemate", "averted" },
            ["end"] = new[] { "stormed", "surrendered", "betrayed", "relieved", "lifted", "abandoned", "hanged", "pardoned", "age", "slain" },
            ["form"] = new[] { "Kingdom", "MerchantRepublic", "LooseAssociation", "Horde" },
            ["terrain"] = new[] { "open", "forest", "hills", "mountain", "marsh", "desert", "snow" },
            ["treaty"] = new[] { "marriage", "foster" },
            ["severity"] = new[] { "mild", "grave", "dire" },
            ["dir"] = new[] { "up", "down" },
            ["role"] = new[] { "Capital", "Colony", "Node", "Post", "Shrine", "Refuge", "Waystation" },
            ["tier.from"] = new[] { "Hamlet", "Village", "Town", "City" },
            ["tier.to"] = new[] { "Hamlet", "Village", "Town", "City" },
            ["source"] = new[] { "displaced", "deserters", "dispossessed", "ruined" },
            ["plot"] = new[] { "sponsorship", "spy_network", "sponsorship_exposed", "sabotage", "assassination", "bribe", "theft", "discovered" },
            ["refused"] = new[] { "marriage", "foster" },
            ["trauma"] = new[] { "war", "conquest", "desecration" },
            ["agenda"] = new[] { "dominion", "ruin", "hunger", "shadow" },
            ["brigands"] = new[] { "yes" },
            ["battle"] = new[] { "yes" },
        };
        foreach (var p in RealmPrefixes) map[p + "culture"] = Cultures.ToArray();
        foreach (var p in new[] { "att.", "def.", "win.", "lose." }) map[p + "loss"] = new[] { "light", "heavy", "grievous", "annihilated" };
        return map;
    }
}
