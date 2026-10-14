# Lineage

Descent math: ancestry shares, implied status, pairwise compatibility.

Namespace `RoguelikeToolkit.World.Core.Lineage`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **AncestryShare**: one `{folk, share}` entry.
- **Ancestry**: shares sorted steeply first; `ResolveImpliedStatus(threshold)` yields the dominant folk or `mixed`.
- **CompatibilityRule**: `blend` / `dominant` / `sterile`.
- **CompatibilityMatrix**: symmetric pair rules with a blend default; `TryUnion` rejects sterile pairs.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Pure and data-free: thresholds and rules arrive as parameters / rows, so this slice never reads `Peoples` data.
