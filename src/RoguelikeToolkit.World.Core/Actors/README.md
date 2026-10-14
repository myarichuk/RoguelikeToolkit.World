# Actors

Simulated people as history building blocks: handles, rolled personality, lineage refs, adult gate.

Namespace `RoguelikeToolkit.World.Core.Actors`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **ActorId**: 1-based stable handle; `default` is "no actor".
- **TraitWeight**: per-trait prior weight (low / balanced / high).
- **ActorTraits**: five OCEAN bytes rolled once from `Rng.Create(seed, actorId)` against the priors.
- **Actor**: folk + ancestry + traits + house id + adult flag + faith id. Adult gate (`RequireAdult`) enforced here.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- No geography, no names: callers pass the folk maturation age as a plain number so this slice never depends on `Peoples`.
- Traits are rolled in fixed O, C, E, A, N order from a dedicated stream, so operator order never changes outcomes.
