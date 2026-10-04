# Names: data-driven people and place names

`FantasyNameGenerator` builds names from YAML tables (`src/RoguelikeToolkit.World.Core/Names/Data/*.yaml`, one file per folk or culture, `Key` = filename). Every name carries its etymology (`Parts`, `Gloss`), is deterministic for a seed, and can render in Native, Latin, Hebrew or Cyrillic script.

```csharp
var styles = FantasyNameGenerator.Styles();            // flat picker list: race + culture resolved to one table
var s = styles.First(x => x.Label.Contains("Hebrew"));
var person = FantasyNameGenerator.GeneratePerson(s.Race, s.Culture, seed: 7, feminine: true);
var themes = FantasyNameGenerator.PersonThemes(s.Race, s.Culture, feminine: true);   // dropdown contents
var place  = FantasyNameGenerator.GeneratePlace("village", new PlaceContext { NearWater = true },
                 s.Race, s.Culture, seed: 7, inspiredBy: new[] { "gold" });
```

`culture` only matters for `human` (and half-bloods, which borrow a human surname); UIs should use `Styles()` instead of two pickers.

## Table schema (all optional unless noted)

| Field | Meaning |
|---|---|
| `Key` (required) | Must equal the filename. |
| `DisplayName` | Label for pickers. |
| `GivenStems` (required) | `{Form, Gloss, Latin?, Tags?, Base?, BaseLatin?}`. Tags `masc`/`fem` gender a name; `solo` = never takes a second stem; `bound` = half a name (`Ald`, `Vladi`) that always takes one; `nopatron` = not usable as a parent name. `Base` is the stem a suffix fuses onto (Пётр -> Петр + ович, Vladi -> Vlad + ovich). |
| `SecondStems`, `CompoundChance` | Optional second element (default 65 percent). Gendered second stems are strict: a feminine name never takes a `masc` ending, so a compound table needs `fem` endings too (Vladi+slava, Ger+linde). Set `CompoundChance: 0` for tables of whole real names (Hebrew, Japanese, Irish...). |
| `Patronymic*`, `PatronymicPrefix` | Marker words. Prefix = separate word (`ben David`), otherwise fused onto the parent's base. `Patronymic: false` turns the slot off. A fused marker counts as a word of the name, so a stem that repeats it (`Spark` with `-spark`) is never picked; a test rejects such dead entries. |
| `FamilyAffixes`, `FamilySuffix`, `FamilyChance` | Surnames. With `FamilySuffix: true` the affix fuses onto a *different* masculine stem. Otherwise it stands alone, and `masc`/`fem` tags pick Ivanov/Ivanova. |
| `Clans`, `ClanChance` | Clan / house / warband slot (default 45 percent human, 80 percent other). Race tables keep one list here; do not duplicate it in `FamilyAffixes`. |
| `Epithets`, `EpithetChance` | Epithet slot (default 0 = off). |
| `FamilyFirst` | Family name is written first (Chinese, Japanese). |
| `Descriptors`, `Nouns` | Place vocabulary. Nouns are tagged with the kinds they name (`village town city river lake mountain hill valley forest hold camp`); every table needs at least one noun per kind (a test enforces it; at runtime a missing kind borrows a related one, e.g. mountain -> hill). Descriptors are tagged with the kinds/terrain they suit and are *excluded* where none fits, so keep generic words (colours, old/new, compass points) untagged or a town only ever gets two names. |
| `PlaceOrder`, `PlaceJoin` | `DescriptorFirst`/`NounFirst`; `Fuse`/`Space`/`Hyphen`. Hebrew is `NounFirst`+`Space` (Kfar Zahav), Welsh `NounFirst`+`Fuse` (Caergwyn). |
| `PlaceFallback` | Default true: fantasy_common tops up thin tables, at a third of the weight of the table's own words so a gnome town stays a "-tinkery" more than a "-burg". Language-faithful tables set false so a Hebrew village is never "Clearwood". |
| `Banned` | Substrings that must never appear (checked in transliterated scripts too). |

Gotcha: in `{...}` flow maps a comma ends the value, so `{Gloss: red, beautiful}` silently drops "beautiful". Write `beautiful red` or quote the value. A test parses every shipped file strictly to catch this.

## What the generator guarantees

- **No repeated roots across slots.** Every root already in a name (form, Latin, fusable base, and its Hebrew and Cyrillic renderings) is off-limits for later slots: no `Ash ... Ashbringer`, `ibn Saqr Banu Saqr` or `Petrovich Petrov`. Optional slots (clan, epithet) are dropped rather than repeat; picks are the same in every script, so a character keeps their identity when the script changes.
- **Clean text.** The finished name is checked with `NameLint` (triple letters, a doubled chunk like `Gustgust`, a word twice, a root repeated across parts, stray spaces, Hebrew final letters mid-word) and rerolled deterministically if it fails, exactly like a banned word. `NameLint.Check` is public; tests run it over every style, script, gender and place kind.
- **Fusion.** Only a doubled boundary *vowel* is dropped (`Eli`+`iel` = `Eliel`); consonants stay (`Shan`+`ning` = `Shanning`, `Fred`+`dochter` = `Freddochter`).

## Name tester

`src/RoguelikeToolkit.World.NameTester` (Avalonia). Each row shows its seed (`#1234`) and any `NameLint` problem; changing an option re-rolls the list on the same seed; *Mixed* gender alternates by seed. **Audit** rolls 400 names with the current settings and reports how many are distinct, the most repeated one and how many are flagged (flagged ones listed first), which is the quick way to find a thin word list. Reload uses `NameDataStore.LoadLenient`: a broken YAML file is reported in the status bar (all problems in its tooltip) and keeps its shipped table, while every other edit still applies.

## Inspiration themes

The "inspired by" list is derived from the loaded YAML (`PersonThemes` / `PlaceThemes`), so it always matches the selected style and gender, and every entry is guaranteed to bias at least one slot the table can emit (stems ruled out by the patronymic marker are not offered). Free text is still accepted by the generator; matching is word-aware (`ash` no longer hits "splash").

## Where the YAML lives at runtime

`NameDataStore.EnsureExtracted()` copies the embedded tables to `%APPDATA%/RoguelikeToolkit.World/Names` so they can be edited, and files there override the embedded ones. A `.shipped.json` manifest records what was last shipped: files you have not touched are upgraded automatically when the library ships newer tables, files you edited are never overwritten, and pre-manifest copies are upgraded with the old content kept as `name.yaml.old`.
