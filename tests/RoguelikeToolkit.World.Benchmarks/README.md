# World benchmarks (backlog C1)

BenchmarkDotNet project over the default `WorldBuilder` pipeline (seed 42).
`MemoryDiagnoser` is on everywhere; numbers are managed-only allocations.

## Suites

| Class | What | Notes |
|---|---|---|
| `FullBuildBenchmarks` | Full build, sizes 3–6 | Default job |
| `LargeBuildBenchmarks` | Full build, size 7 | Reduced job (1 launch / 1 warmup / 3 iterations); ~5 s per build |
| `StageBenchmarks` | Per-stage `Execute` on a size-3 world | Each stage re-runs on a map pre-baked through its prerequisites |
| `QueryBenchmarks` | `GetTileIndex` vs `GetTileIndexExact`, `GetAdjacent`, `DeriveRegionMap`/`DeriveLocalMap`, `SpatialIndex` build, `QueryRadius`, `NearestRiver`, `ScoreCitySites` | One size-4 world |
| `SmokeBenchmarks` | Size-3 build + one lookup | CI bitrot check only, not a baseline source |

## Baseline workflow

Full suites are local-only (CI timing is too noisy for regression gates):

```powershell
dotnet run --project tests/RoguelikeToolkit.World.Benchmarks -c Release
```

Filter to one suite:

```powershell
dotnet run --project tests/RoguelikeToolkit.World.Benchmarks -c Release -- --filter "*QueryBenchmarks*"
```

CI smoke (what `ci.yml` runs):

```powershell
dotnet run --project tests/RoguelikeToolkit.World.Benchmarks -c Release --no-build -- --filter "*SmokeBenchmarks*" --launchCount 1 --warmupCount 1 --iterationCount 1
```

Results land in `BenchmarkDotNet.Artifacts/results/` (gitignored). When
reporting numbers (perf PRs, TODO-log baselines), paste the Markdown table
plus: machine, OS, SDK (`dotnet --info`), configuration, and date.
