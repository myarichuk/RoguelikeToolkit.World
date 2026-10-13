# Names

Fantasy name generation: culture/race data (embedded YAML in `Data/`), morpheme-based person and place names, and lint.

Namespace `RoguelikeToolkit.World.Core.Names`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **FantasyNameGenerator (NameGenerator.cs)**: Public facade: configuration, catalog queries, `Generate*`.
- **PersonNameBuilder, PlaceNameBuilder, MorphemePicker, NameText (internal)**: Assembly, selection and text helpers behind the facade.
- **NameDataStore, CultureData, Cultures, FantasyRaces, PlaceKinds**: Data model and catalogs.
- **NameLint**: Quality checks over generated names.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- See `docs/NAMES.md` for the user-facing behaviour.
- YAML resources under `Data/` are embedded; their resource names depend on the folder, so do not move them.
