# Climate

Temperature, precipitation, wind, ocean currents, sea ice and glaciation.

Namespace `RoguelikeToolkit.World.Core.Climate`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **ClimateStage**: Stage order 16. `Run` is deliberately one long method: its phases share about a dozen locals in the hottest loop.
- **ClimateRefreshStage**: Order 17; runs the climate model a second time on the eroded relief, so hydrology and biomes see rain shadows that match the final surface.
- **ClimateModel, ClimateInfo, ClimateLayer**: Model and per-tile data.
- **OceanCurrents, SeaIce, Glaciology**: Sub-models.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
