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
| `GivenStems` (required) | `{Form, Gloss, Latin?, Tags?, Base?, BaseLatin?}`. Tags `masc`/`fem` gender a name; `solo` = never takes a second stem; `nopatron` = not usable as a parent name. `Base` is the stem a suffix fuses onto (Пётр -> Петр + ович). |
| `SecondStems`, `CompoundChance` | Optional second element (default 65 percent). Gendered second stems are strict: a feminine name never takes a `masc` ending. Set `CompoundChance: 0` for tables of whole real names (Hebrew, Japanese, Irish...). |
| `Patronymic*`, `PatronymicPrefix` | Marker words. Prefix = separate word (`ben David`), otherwise fused onto the parent's base. `Patronymic: false` turns the slot off. |
| `FamilyAffixes`, `FamilySuffix`, `FamilyChance` | Surnames. With `FamilySuffix: true` the affix fuses onto a *different* masculine stem. Otherwise it stands alone, and `masc`/`fem` tags pick Ivanov/Ivanova. |
| `Clans`, `ClanChance` | Clan / house / warband slot (default 45 percent human, 80 percent other). Race tables keep one list here; do not duplicate it in `FamilyAffixes`. |
| `Epithets`, `EpithetChance` | Epithet slot (default 0 = off). |
| `FamilyFirst` | Family name is written first (Chinese, Japanese). |
| `Descriptors`, `Nouns` | Place vocabulary. Nouns are tagged with the kinds they name (`village town city river lake mountain hill valley forest hold camp`); descriptors are tagged with the terrain they suit, and untagged = generic. |
| `PlaceOrder`, `PlaceJoin` | `DescriptorFirst`/`NounFirst`; `Fuse`/`Space`/`Hyphen`. Hebrew is `NounFirst`+`Space` (Kfar Zahav), Welsh `NounFirst`+`Fuse` (Caergwyn). |
| `PlaceFallback` | Default true: fantasy_common tops up thin tables. Language-faithful tables set false so a Hebrew village is never "Clearwood". |
| `Banned` | Substrings that must never appear (checked in transliterated scripts too). |

Gotcha: in `{...}` flow maps a comma ends the value, so `{Gloss: red, beautiful}` silently drops "beautiful". Write `beautiful red` or quote the value. A test parses every shipped file strictly to catch this.

## Inspiration themes

The "inspired by" list is derived from the loaded YAML (`PersonThemes` / `PlaceThemes`), so it always matches the selected style and gender, and every entry is guaranteed to bias at least one slot the table can emit. Free text is still accepted by the generator; matching is word-aware (`ash` no longer hits "splash").

## Where the YAML lives at runtime

`NameDataStore.EnsureExtracted()` copies the embedded tables to `%APPDATA%/RoguelikeToolkit.World/Names` so they can be edited, and files there override the embedded ones. A `.shipped.json` manifest records what was last shipped: files you have not touched are upgraded automatically when the library ships newer tables, files you edited are never overwritten, and pre-manifest copies are upgraded with the old content kept as `name.yaml.old`.
