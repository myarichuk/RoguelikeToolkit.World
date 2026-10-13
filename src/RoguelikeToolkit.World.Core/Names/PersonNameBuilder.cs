using RoguelikeToolkit.World.Core.Primitives;
using static RoguelikeToolkit.World.Core.Names.FantasyNameGenerator;
using static RoguelikeToolkit.World.Core.Names.NameText;
using static RoguelikeToolkit.World.Core.Names.MorphemePicker;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Assembles one person name (given name, patronymic/clan/family parts) from a culture table.</summary>
internal static class PersonNameBuilder
{
    internal static GeneratedName BuildPerson(
        NameDataStore store, string race, string culture,
        int seed, string? lineage, bool feminine, int salt, HashSet<string> tokens, NameScript script)
    {
        if (string.IsNullOrWhiteSpace(race)) race = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(culture)) culture = Cultures.FantasyCommon;
        var table = store.Get(TableKey(race, culture, store));
        var rng = Rng.Create(seed, salt ^ (StableHash(race) * 7919) ^ (StableHash(culture) * 4789));
        var effLineage = NormalizeLineage(string.IsNullOrWhiteSpace(lineage) ? table.LineageDefault : lineage);
        var parts = new List<NamePart>();

        // Fused patronymic markers count as words already in the name: a "spark" second stem
        // next to a "-spark" patronymic reads "Glowspark Forgespark".
        var used = new RootSet();
        if (table.Patronymic && !table.PatronymicPrefix)
            used.AddMarker(table, feminine);

        // Given name: stem (+ second stem for compound cultures), biased by inspirations.
        // A bare "Gust" beside a "-gust" patronymic is the same stutter, so the markers filter stems too.
        var givenPool = ForGender(table.GivenStems, feminine);
        var freshGiven = givenPool.Where(m => !used.Clashes(m)).ToList();
        if (freshGiven.Count > 0) givenPool = freshGiven;
        var stem = PickBiased(ref rng, givenPool, tokens) ?? new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        MorphemeYaml? second = null;
        if (table.SecondStems.Count > 0 && !HasTag(stem, "solo"))
        {
            // A "bound" stem is only half a name ("Ald", "Medved"): it always takes a second element.
            bool roll = rng.NextUInt(100) < (uint)table.CompoundChance;
            if (roll || HasTag(stem, "bound"))
            {
                // Strict, unlike given stems: a feminine name must never take a masculine ending ("Layla al-Din").
                var secondPool = table.SecondStems.Where(m => GenderOf(m) != (feminine ? 1 : -1) && !used.Clashes(m)).ToList();
                second = PickSecond(ref rng, secondPool, tokens, stem);
            }
        }
        if (second is null && HasTag(stem, "bound"))
        {
            // No ending fits (every draw clashed, or none suits this gender): use a stem that can stand alone.
            var free = givenPool.Where(m => !HasTag(m, "bound")).ToList();
            if (free.Count > 0) stem = PickBiased(ref rng, free, tokens)!;
        }
        used.Add(stem);
        if (second is not null) used.Add(second);
        var stemShown = Cap(Display(stem, script));
        string given;
        if (second is null)
        {
            given = stemShown;
        }
        else
        {
            var secondShown = Display(second, script);
            // Particles that are words of their own ("al-Din") join with a space,
            // plain stem endings ("gar", "mir") fuse ("Thrungar", "Vladimir").
            given = secondShown.Contains(' ') || secondShown.Contains('-')
                ? stemShown + " " + secondShown
                : Fuse(stemShown, secondShown, dedupe: true);
        }
        var givenGloss = second is null ? stem.Gloss : JoinGloss(stem.Gloss, second.Gloss);
        parts.Add(new NamePart { Form = given, Gloss = givenGloss, Role = NamePartRoles.Given });

        // Parent for the patronymic/matronymic slot: the right gender for the line,
        // never the child's own stem ("Vess Vessseth" is not a lineage, it is a typo).
        bool matri = effLineage.Equals(Lineages.Matrilineal, StringComparison.OrdinalIgnoreCase);
        string? parentForm = null;
        bool human = race.Equals(FantasyRaces.Human, StringComparison.OrdinalIgnoreCase);
        // Tables like Hebrew use either a patronymic or a family name, never both:
        // the lineage slot is rolled before the parent is picked so a fired
        // clan/family slot suppresses the patronymic ("David Cohen", never
        // "David ben Yosef Cohen").
        bool preRolledLineage = table.FamilyReplacesPatronymic && table.Patronymic;
        bool preClanned = false, preFamily = false;
        if (preRolledLineage)
        {
            uint preClanChance = (uint)(table.ClanChance >= 0 ? table.ClanChance : 45);
            preClanned = table.Clans.Count > 0 && rng.NextUInt(100) < preClanChance;
            if (!preClanned)
                preFamily = table.FamilyAffixes.Count > 0 && rng.NextUInt(100) < (uint)table.FamilyChance;
        }
        bool lineageTaken = preClanned || preFamily;
        if (table.Patronymic && !lineageTaken)
        {
            var marker = Marker(table, feminine, script);
            // Never the child's own stem or anything sharing a root with the name so far, which
            // includes a fused marker: "Gust"+"gustess" is a stutter, not a lineage.
            var parentPool = table.GivenStems.Where(m => !HasTag(m, "nopatron")
                    && GenderOf(m) != (matri ? 1 : -1)
                    && !used.Clashes(m)).ToList();
            if (parentPool.Count == 0)
                parentPool = table.GivenStems.Where(m => !m.Form.Equals(stem.Form, StringComparison.OrdinalIgnoreCase)).ToList();
            if (parentPool.Count == 0) parentPool = table.GivenStems;
            var parent = PickBiased(ref rng, parentPool, tokens) ?? stem;
            parentForm = parent.Form;
            used.Add(parent);
            var gloss = $"{(feminine ? "daughter of " : "son of ")}{StripOf(parent.Gloss)}";
            var role = matri ? NamePartRoles.Matronymic : NamePartRoles.Patronymic;
            if (table.PatronymicPrefix)
            {
                // "mac Bran", "ben David", "ibn Karim": the marker is its own word.
                parts.Add(new NamePart { Form = $"{marker} {Cap(Display(parent, script))}", Gloss = gloss, Role = role });
            }
            else
            {
                // "Bran"+"son", "Petr"+"ovich": fuse onto the base, unless the marker is itself spaced.
                string sep = marker.Contains(' ') ? " " : "";
                var baseShown = Cap(DisplayBase(parent, script));
                parts.Add(new NamePart
                {
                    Form = sep.Length > 0 ? baseShown + sep + marker : Fuse(baseShown, marker, dedupe: true),
                    Gloss = gloss, Role = role,
                });
            }
        }

        // Clan / family / warband / epithet slots. Race-specific behaviour lives in
        // the YAML (ClanChance, EpithetChance, FamilyChance, FamilyReplacesPatronymic);
        // only the half-bloods, which borrow a human family name, are special-cased by race.
        // A person name holds at most three parts: the epithet below yields when a
        // patronymic plus a clan/family name already filled the lineage slot.
        if (race.Equals(FantasyRaces.HalfElf, StringComparison.OrdinalIgnoreCase))
        {
            AddHumanFamily(ref rng, store, culture, parts, script, feminine, used);
        }
        else if (race.Equals(FantasyRaces.HalfOrc, StringComparison.OrdinalIgnoreCase))
        {
            if (rng.NextUInt(100) < 50 && table.Epithets.Count > 0)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script, used);
            else
                AddHumanFamily(ref rng, store, culture, parts, script, feminine, used);
        }
        else
        {
            uint clanChance = (uint)(table.ClanChance >= 0 ? table.ClanChance : human ? 45 : 80);
            bool clanned = preRolledLineage ? preClanned : table.Clans.Count > 0 && rng.NextUInt(100) < clanChance;
            if (clanned)
            {
                // Optional slot: when every clan repeats a word already in the name, leave it out.
                var clan = PickAvoiding(ref rng, table.Clans, tokens, used);
                if (clan is not null)
                {
                    used.Add(clan);
                    parts.Add(new NamePart
                    {
                        Form = Display(clan, script), Gloss = clan.Gloss,
                        Role = human ? NamePartRoles.Family : NamePartRoles.Clan,
                    });
                }
            }
            else if (preRolledLineage ? preFamily : table.FamilyAffixes.Count > 0 && rng.NextUInt(100) < (uint)table.FamilyChance)
            {
                if (table.FamilySuffix)
                {
                    var affixes = ForGender(table.FamilyAffixes, feminine);
                    var freshAffixes = affixes.Where(m => !used.Clashes(m)).ToList();
                    var fam = PickBiased(ref rng, freshAffixes.Count > 0 ? freshAffixes : affixes, tokens)!;
                    // True suffixes fuse onto a *different* stem ("Ald"+"bury", "Mil"+"ov"):
                    // a surname built from a root already in the name reads as a stutter,
                    // and one built from a feminine given name ("Olgaova") is simply wrong.
                    var famPool = table.GivenStems.Where(m => GenderOf(m) != -1 && !used.Clashes(m)
                        && !SharesRoot(m.Form, fam.Form)).ToList();
                    if (famPool.Count == 0)
                        famPool = table.GivenStems.Where(m => GenderOf(m) != -1
                            && !m.Form.Equals(stem.Form, StringComparison.OrdinalIgnoreCase)
                            && (parentForm is null || !m.Form.Equals(parentForm, StringComparison.OrdinalIgnoreCase))).ToList();
                    var fb = PickBiased(ref rng, famPool.Count > 0 ? famPool : table.GivenStems, tokens) ?? stem;
                    used.Add(fb);
                    parts.Add(new NamePart
                    {
                        Form = Fuse(Cap(DisplayBase(fb, script)), Display(fam, script), dedupe: true),
                        Gloss = AffixGloss(fb.Gloss, fam.Gloss),
                        Role = NamePartRoles.Family,
                    });
                }
                else
                {
                    // House / family / court names stand alone ("Starhaven", "Li").
                    var fam = PickAvoiding(ref rng, ForGender(table.FamilyAffixes, feminine), tokens, used);
                    if (fam is not null)
                    {
                        used.Add(fam);
                        parts.Add(new NamePart
                        {
                            Form = Display(fam, script), Gloss = fam.Gloss,
                            Role = human ? NamePartRoles.Family : NamePartRoles.Clan,
                        });
                    }
                }
            }
            if (table.Epithets.Count > 0 && parts.Count < 3 && rng.NextUInt(100) < (uint)table.EpithetChance)
                AddPickBiased(ref rng, parts, table.Epithets, NamePartRoles.Epithet, tokens, script, used);
        }

        string text = JoinPerson(table, parts);
        return new GeneratedName
        {
            Text = text,
            Race = race,
            Culture = culture,
            Lineage = effLineage,
            Provenance = new NameProvenance { Inspiration = "original", Note = table.Provenance },
            Inspirations = MatchedTokens(parts, tokens),
            Parts = parts,
            Seed = seed,
        };
    }

    internal static void AddHumanFamily(
        ref Rng rng, NameDataStore store, string culture, List<NamePart> parts, NameScript script, bool feminine,
        RootSet used)
    {
        var humanTable = store.ResolveCulture(
            string.IsNullOrWhiteSpace(culture) ? Cultures.FantasyCommon : culture, Cultures.All);
        var common = store.Get(Cultures.FantasyCommon);
        var fam = Pick(ref rng, humanTable.Clans.Concat(humanTable.FamilyAffixes)
                      .Where(m => GenderOf(m) != (feminine ? 1 : -1) && !used.Clashes(m)).ToList())
            ?? Pick(ref rng, common.FamilyAffixes);
        if (fam is null) return;
        // Suffix-only tables ("bury", "ov") have no standalone surname, so borrow a stem to carry them.
        string form = Display(fam, script);
        string gloss = fam.Gloss;
        if (humanTable.FamilySuffix && humanTable.FamilyAffixes.Contains(fam) && humanTable.GivenStems.Count > 0)
        {
            var carriers = humanTable.GivenStems.Where(m => GenderOf(m) != -1).ToList();
            var fresh = carriers.Where(m => !used.Clashes(m)).ToList();
            var b = Pick(ref rng, fresh.Count > 0 ? fresh : carriers) ?? humanTable.GivenStems[0];
            form = Fuse(Cap(DisplayBase(b, script)), form, dedupe: true);
            gloss = AffixGloss(b.Gloss, fam.Gloss);
        }
        parts.Add(new NamePart { Form = form, Gloss = gloss + " (human side)", Role = NamePartRoles.Family });
    }

    internal static string JoinPerson(CultureData table, List<NamePart> parts)
    {
        if (table.FamilyFirst)
        {
            var fam = parts.FirstOrDefault(p => p.Role is NamePartRoles.Family or NamePartRoles.Clan);
            var rest = parts.Where(p => p != fam).ToList();
            return fam is null ? string.Join(" ", rest.Select(p => p.Form)) : $"{fam.Form} {string.Join(" ", rest.Select(p => p.Form))}".Trim();
        }
        return string.Join(" ", parts.Select(p => p.Form));
    }
}
