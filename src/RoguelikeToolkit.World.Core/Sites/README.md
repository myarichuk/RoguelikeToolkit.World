# Sites

Injector passes that place ruins, settlements, mines and landmarks over region/local maps.

Namespace `RoguelikeToolkit.World.Core.Sites`, layer rank 5 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **SiteInjectorPipeline, ISiteInjector, SiteInjectorBase**: Ordered passes; duplicate `Order` throws.
- **SiteInjectionContext**: Address, parent, bounds, existing sites and an injector-private RNG.
- **SiteCatalog, PlacedSite**: The overlay result; never stored in the field store.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- An injector's RNG derives from (injector id, address), so adding or removing an injector never reshuffles others.
