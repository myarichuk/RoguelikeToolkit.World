# Localization

Locale tables for user-visible strings (`en`, `he`, `ru` embedded YAML).

Namespace `RoguelikeToolkit.World.Core.Localization`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **LocaleStore**: Lookup of strings by key and locale.
- **LocaleDataException**: Thrown for malformed locale data.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Embedded resource names depend on this folder; do not move the YAML files.
