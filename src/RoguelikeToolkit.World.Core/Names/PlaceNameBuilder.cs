using RoguelikeToolkit.World.Core.Primitives;
using static RoguelikeToolkit.World.Core.Names.FantasyNameGenerator;
using static RoguelikeToolkit.World.Core.Names.NameText;
using static RoguelikeToolkit.World.Core.Names.MorphemePicker;

namespace RoguelikeToolkit.World.Core.Names;

/// <summary>Assembles one place name (descriptor + noun, feature kinds, softening) from a culture table.</summary>
internal static class PlaceNameBuilder
{
    internal static GeneratedName BuildPlace(
        NameDataStore store, string kind, PlaceContext ctx,
        string race, string culture, int seed, int salt, HashSet<string> tokens, NameScript script)
    {
        if (string.IsNullOrWhiteSpace(race)) race = FantasyRaces.Human;
        if (string.IsNullOrWhiteSpace(culture)) culture = Cultures.FantasyCommon;
        var table = store.Get(TableKey(race, culture, store));
        var common = store.Get(Cultures.FantasyCommon);
        var rng = Rng.Create(seed, salt ^ (StableHash(kind) * 311) ^ (StableHash(race) * 701));
        var tags = TagsFor(ctx, kind);
        var kindTags = KindTags(kind);

        // Vocabulary: the table's own, topped up from fantasy_common only for
        // tables that opt in (a Japanese village must never be "Clearwood").
        var descs = table.PlaceFallback ? table.Descriptors.Concat(common.Descriptors).ToList() : table.Descriptors.ToList();
        var nouns = table.PlaceFallback ? table.Nouns.Concat(common.Nouns).ToList() : table.Nouns.ToList();
        if (descs.Count == 0) descs = common.Descriptors.ToList();
        if (nouns.Count == 0) nouns = common.Nouns.ToList();

        // Nouns must fit the kind of place (a river is not a "-hold"). A table with no word
        // for this kind borrows a neighbouring kind (a mountain can be a "-hill", a camp a
        // "-hold") before it falls back to the whole list; shipped tables cover every kind.
        var fitNouns = nouns.Where(n => kindTags.Any(k => HasTag(n, k))).ToList();
        if (fitNouns.Count == 0) fitNouns = nouns.Where(n => RelatedKinds(kind).Any(k => HasTag(n, k))).ToList();
        if (fitNouns.Count == 0) fitNouns = nouns;

        // Topped-up tables keep their own voice: a gnome town is a "-tinkery" far more often
        // than a generic "-burg", even though fantasy_common has more words.
        bool topUp = table.PlaceFallback && !ReferenceEquals(table, common);
        var own = topUp ? new HashSet<MorphemeYaml>(table.Descriptors.Concat(table.Nouns), ReferenceEqualityComparer.Instance) : null;
        int Own(MorphemeYaml m) => own is not null && own.Contains(m) ? 3 : 1;

        int DescWeight(MorphemeYaml d)
        {
            // Untagged descriptors are generic ("Great"); tagged ones must fit the kind or terrain.
            int overlap = d.Tags.Count(t => tags.Contains(t));
            int w = d.Tags.Count == 0 ? 1 : overlap > 0 ? 1 + 2 * overlap : 0;
            return w == 0 ? 5 * CountMatches(d, tokens) : w * Own(d) + 5 * CountMatches(d, tokens);
        }
        int NounWeight(MorphemeYaml n)
        {
            int w = 1;
            for (int i = 0; i < kindTags.Length; i++)
                if (HasTag(n, kindTags[i])) w += i == 0 ? 3 : 1;
            w += n.Tags.Count(t => tags.Contains(t) && !kindTags.Contains(t, StringComparer.OrdinalIgnoreCase));
            return w * Own(n) + 5 * CountMatches(n, tokens);
        }

        MorphemeYaml? desc = null, noun = null;
        for (int i = 0; i < 8; i++)
        {
            desc = WeightedPick(ref rng, descs, DescWeight);
            noun = WeightedPick(ref rng, fitNouns, NounWeight);
            if (desc is null || noun is null) break;
            // "Stonestone", "Howlinghowl" (howl-haunted + howling hill): one idea twice.
            if (Spellings(desc).Any(d => Spellings(noun).Any(n => SharesRoot(d, n)))) continue;
            var nounWords = NameThemes.Words(noun.Gloss).ToList();
            if (NameThemes.Words(desc.Gloss).Any(w => nounWords.Any(n => NameThemes.WordMatches(w, n)))) continue;
            break;
        }
        desc ??= new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        noun ??= new MorphemeYaml { Form = "ford", Gloss = "crossing" };

        bool nounFirst = table.PlaceOrder.Equals("NounFirst", StringComparison.OrdinalIgnoreCase);
        // Mutate the source spelling, not the rendering, so every script carries it ("Pontddu", "Понтддю").
        if (nounFirst && HasTag(noun, "fem") && table.PlaceMutation.Equals("Soft", StringComparison.OrdinalIgnoreCase))
            desc = AfterFeminine(desc, noun);
        var descShown = Display(desc, script);
        var nounShown = Display(noun, script);
        var first = CapWord(nounFirst ? nounShown : descShown);
        var secondRaw = nounFirst ? descShown : nounShown;
        string join = table.PlaceJoin.ToLowerInvariant();
        bool spaced = join == "space" || secondRaw.Contains(' ') || secondRaw.Contains('-') || first.Contains(' ');
        // A seam that would run three letters together is written with a hyphen, as Welsh does
        // ("Rhyd-ddu", "Pwll-llwyd"), rather than losing a letter ("Rhyddu").
        string text = join == "hyphen" || (!spaced && TripleAtSeam(first, secondRaw)) ? first + "-" + secondRaw
            : spaced ? first + " " + CapWord(secondRaw)
            // Single-word compounds read "Clearford", not "ClearFord".
            : Fuse(first, secondRaw.ToLowerInvariant(), dedupe: false);

        var descPart = new NamePart { Form = CapWord(descShown), Gloss = desc.Gloss, Role = NamePartRoles.Descriptor };
        var nounPart = new NamePart { Form = CapWord(nounShown), Gloss = noun.Gloss, Role = NamePartRoles.Geography };
        var parts = nounFirst ? new List<NamePart> { nounPart, descPart } : new List<NamePart> { descPart, nounPart };
        var near = tags.Where(t => !kindTags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        return new GeneratedName
        {
            Text = text,
            Race = race,
            Culture = culture,
            Lineage = NormalizeLineage(table.LineageDefault),
            Provenance = new NameProvenance { Inspiration = "original", Note = $"{kind} near {string.Join("/", near.Count > 0 ? near : new List<string> { "open land" })}; {table.Provenance}" },
            // Confirm against the chosen morphemes (not just the text) so terrain tags like "dark" count.
            Inspirations = tokens.Where(t => NameThemes.Matches(desc, t) || NameThemes.Matches(noun, t))
                .OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            Parts = parts,
            Seed = seed,
        };
    }

    /// <summary>
    /// Assembles one hybrid place name for a conquest or settled-nomad city: the
    /// descriptor comes from the superstrate table, the noun from the substrate
    /// table, joined by the superstrate's order. The ruling voice names the
    /// place; the land keeps its old noun.
    /// </summary>
    internal static GeneratedName BuildHybridPlace(
        NameDataStore store, string kind, PlaceContext ctx,
        CultureData superstrate, CultureData substrate,
        string race, string culture, int seed, int salt, HashSet<string> tokens, NameScript script)
    {
        var common = store.Get(Cultures.FantasyCommon);
        var rng = Rng.Create(seed, salt ^ (StableHash(kind) * 311) ^ (StableHash(superstrate.Key) * 701) ^ (StableHash(substrate.Key) * 131));
        var tags = TagsFor(ctx, kind);
        var kindTags = KindTags(kind);

        // Vocabulary: descriptors from the ruling table (topped up when it opts
        // in), nouns from the settled table (topped up when it opts in).
        var descs = superstrate.PlaceFallback ? superstrate.Descriptors.Concat(common.Descriptors).ToList() : superstrate.Descriptors.ToList();
        var subNouns = substrate.PlaceFallback ? substrate.Nouns.Concat(common.Nouns).ToList() : substrate.Nouns.ToList();
        if (descs.Count == 0) descs = common.Descriptors.ToList();
        if (subNouns.Count == 0) subNouns = common.Nouns.ToList();

        // Nouns must fit the kind of place, as in the single-table build.
        var fitNouns = subNouns.Where(n => kindTags.Any(k => HasTag(n, k))).ToList();
        if (fitNouns.Count == 0) fitNouns = subNouns.Where(n => RelatedKinds(kind).Any(k => HasTag(n, k))).ToList();
        if (fitNouns.Count == 0) fitNouns = subNouns;

        // Topped-up tables keep their own voice on their own side.
        var ownDescs = superstrate.PlaceFallback && !ReferenceEquals(superstrate, common)
            ? new HashSet<MorphemeYaml>(superstrate.Descriptors, ReferenceEqualityComparer.Instance) : null;
        var ownNouns = substrate.PlaceFallback && !ReferenceEquals(substrate, common)
            ? new HashSet<MorphemeYaml>(substrate.Nouns, ReferenceEqualityComparer.Instance) : null;
        int OwnDesc(MorphemeYaml m) => ownDescs is not null && ownDescs.Contains(m) ? 3 : 1;
        int OwnNoun(MorphemeYaml m) => ownNouns is not null && ownNouns.Contains(m) ? 3 : 1;

        int DescWeight(MorphemeYaml d)
        {
            int overlap = d.Tags.Count(t => tags.Contains(t));
            int w = d.Tags.Count == 0 ? 1 : overlap > 0 ? 1 + 2 * overlap : 0;
            return w == 0 ? 5 * CountMatches(d, tokens) : w * OwnDesc(d) + 5 * CountMatches(d, tokens);
        }
        int NounWeight(MorphemeYaml n)
        {
            int w = 1;
            for (int i = 0; i < kindTags.Length; i++)
                if (HasTag(n, kindTags[i])) w += i == 0 ? 3 : 1;
            w += n.Tags.Count(t => tags.Contains(t) && !kindTags.Contains(t, StringComparer.OrdinalIgnoreCase));
            return w * OwnNoun(n) + 5 * CountMatches(n, tokens);
        }

        MorphemeYaml? desc = null, noun = null;
        for (int i = 0; i < 8; i++)
        {
            desc = WeightedPick(ref rng, descs, DescWeight);
            noun = WeightedPick(ref rng, fitNouns, NounWeight);
            if (desc is null || noun is null) break;
            if (Spellings(desc).Any(d => Spellings(noun).Any(n => SharesRoot(d, n)))) continue;
            var nounWords = NameThemes.Words(noun.Gloss).ToList();
            if (NameThemes.Words(desc.Gloss).Any(w => nounWords.Any(n => NameThemes.WordMatches(w, n)))) continue;
            break;
        }
        desc ??= new MorphemeYaml { Form = "Ash", Gloss = "ash tree" };
        noun ??= new MorphemeYaml { Form = "ford", Gloss = "crossing" };

        // The superstrate's language shapes the join.
        bool nounFirst = superstrate.PlaceOrder.Equals("NounFirst", StringComparison.OrdinalIgnoreCase);
        if (nounFirst && HasTag(noun, "fem") && superstrate.PlaceMutation.Equals("Soft", StringComparison.OrdinalIgnoreCase))
            desc = AfterFeminine(desc, noun);
        var descShown = Display(desc, script);
        var nounShown = Display(noun, script);
        var first = CapWord(nounFirst ? nounShown : descShown);
        var secondRaw = nounFirst ? descShown : nounShown;
        string join = superstrate.PlaceJoin.ToLowerInvariant();
        bool spaced = join == "space" || secondRaw.Contains(' ') || secondRaw.Contains('-') || first.Contains(' ');
        string text = join == "hyphen" || (!spaced && TripleAtSeam(first, secondRaw)) ? first + "-" + secondRaw
            : spaced ? first + " " + CapWord(secondRaw)
            : Fuse(first, secondRaw.ToLowerInvariant(), dedupe: false);

        var descPart = new NamePart { Form = CapWord(descShown), Gloss = desc.Gloss, Role = NamePartRoles.Descriptor };
        var nounPart = new NamePart { Form = CapWord(nounShown), Gloss = noun.Gloss, Role = NamePartRoles.Geography };
        var parts = nounFirst ? new List<NamePart> { nounPart, descPart } : new List<NamePart> { descPart, nounPart };
        var near = tags.Where(t => !kindTags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        return new GeneratedName
        {
            Text = text,
            Race = race,
            Culture = culture,
            Lineage = NormalizeLineage(superstrate.LineageDefault),
            Provenance = new NameProvenance { Inspiration = "original", Note = $"hybrid {kind} near {string.Join("/", near.Count > 0 ? near : new List<string> { "open land" })}: {superstrate.Key} over {substrate.Key}; {superstrate.Provenance}" },
            Inspirations = tokens.Where(t => NameThemes.Matches(desc, t) || NameThemes.Matches(noun, t))
                .OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            Parts = parts,
            Seed = seed,
        };
    }

    /// <summary>A descriptor as it reads after a feminine noun: its feminine form, soft-mutated.</summary>
    internal static MorphemeYaml AfterFeminine(MorphemeYaml desc, MorphemeYaml noun)
    {
        var form = string.IsNullOrWhiteSpace(desc.FemForm) ? desc.Form : desc.FemForm;
        return new MorphemeYaml
        {
            Form = SoftMutate(form, noun.Form),
            Gloss = desc.Gloss,
            Tags = desc.Tags,
        };
    }

    internal static HashSet<string> TagsFor(PlaceContext ctx, string kind)
    {
        var tags = new HashSet<string>(KindTags(kind), StringComparer.OrdinalIgnoreCase);
        // A sea is open water: it draws on water words ("Grey", "Still") as well as its own.
        if (kind.Equals(PlaceKinds.Sea, StringComparison.OrdinalIgnoreCase)) tags.Add("water");
        if (ctx.NearWater) { tags.Add("water"); tags.Add("river"); tags.Add("lake"); }
        if (ctx.NearForest) tags.Add("forest");
        if (ctx.DarkForest) { tags.Add("dark"); tags.Add("forest"); }
        if (ctx.Highland) { tags.Add("high"); tags.Add("mountain"); tags.Add("hill"); }
        if (ctx.Valley) tags.Add("valley");
        if (ctx.HasDeposit) tags.Add("deposit");
        if (ctx.Dangerous) tags.Add("danger");
        return tags;
    }

    /// <summary>Noun tags that fit a place kind, best first (a hold prefers "hold", then any "town" word).</summary>
    internal static string[] KindTags(string kind) => kind.ToLowerInvariant() switch
    {
        PlaceKinds.River => new[] { "river" },
        PlaceKinds.Lake => new[] { "lake" },
        PlaceKinds.Sea => new[] { "sea" },
        PlaceKinds.Mountain => new[] { "mountain" },
        PlaceKinds.Hill => new[] { "hill" },
        PlaceKinds.Valley => new[] { "valley" },
        PlaceKinds.Forest => new[] { "forest" },
        PlaceKinds.Town => new[] { "town" },
        PlaceKinds.City => new[] { "city", "town" },
        PlaceKinds.Hold => new[] { "hold", "town" },
        PlaceKinds.Camp => new[] { "camp", "hill" },
        _ => new[] { "village" },
    };

    /// <summary>Fallback kinds for a table with no noun for <paramref name="kind"/>, nearest first.</summary>
    internal static string[] RelatedKinds(string kind) => kind.ToLowerInvariant() switch
    {
        PlaceKinds.River => new[] { "water", "lake" },
        PlaceKinds.Lake => new[] { "water" },
        PlaceKinds.Sea => new[] { "water", "lake" },
        PlaceKinds.Mountain => new[] { "hill", "high" },
        PlaceKinds.Hill => new[] { "mountain" },
        PlaceKinds.Valley => new[] { "village" },
        PlaceKinds.Forest => new[] { "dark" },
        PlaceKinds.Town => new[] { "city", "village" },
        PlaceKinds.City => new[] { "hold" },
        PlaceKinds.Hold => new[] { "city", "camp" },
        PlaceKinds.Camp => new[] { "hold", "village" },
        _ => new[] { "town" },
    };
}
