# Weather — design plan

Status: **plan, no code.** Companion to [REVIEW_2026-10.md](REVIEW_2026-10.md) (W0 depends on its fixes).

## 1. Goal

Weather that a game can query at **any tile, any time**, with weather systems that are physically
plausible:

- seasons, monsoons and moving rain belts;
- extratropical cyclones with fronts travelling along the storm tracks;
- tropical cyclones that form over warm water, track west, recurve and die over land;
- local phenomena: thunderstorms, blizzards, fog off cold currents, dust storms, föhn, lake-effect snow.

It must stay inside the library's rules:

- **Deterministic and random-access.** `Weather(seed, t, x)` is a pure function. A save stores the
  clock, nothing else. Querying day 900 never requires simulating days 0..899, and query order never
  changes the answer.
- **Storageless by default.** It derives from the climate layers plus a small per-year storm catalogue
  (cached, recomputable). There is no new dense layer in `WorldDataStore` unless a host opts into
  baking one.
- **Physical units at the boundary**: °C, hPa, m/s and mm/h. The normalised fields stay inside.
- **`DetMath` only**, integer time, and `netstandard2.1` compatible. The Unity plan depends on it.
- **The generation pipeline does not change.** Existing worlds stay bit-for-bit identical. Any
  feedback into climate (Climate v2) is a separate opt-in, see §8.

## 2. Non-goals

- A GCM. There is no primitive-equation integration and no vertical column. Everything is diagnostic
  or kinematic (prescribed dynamics driven by derived fields), tuned to look right statistically.
- Bit-exact agreement between the planet tier and region/local downscaling beyond "local = planet +
  deterministic detail".
- Weather feeding back into geology. Erosion and hydrology keep using annual means.

## 3. What exists to build on

`ClimateInfo` per tile:

- annual-mean `Temperature` (normalised, **clamped**) and `Precipitation` (dimensionless);
- a prescribed tangent `Wind` (zonal cells, unit speed, steps at the cell edges);
- `SeasonalRange`, `RainSeasonality` and a hemisphere-free `RainPhase`;
- `OceanHeat` and `Current*` (a Stommel gyre solution).

`PlanetParameters` carries obliquity, flux, rotation rate and custom insolation. `SphereNoise`
provides 3D fBm. `ClimateModel` holds the analytic insolation (North 1975 P2 expansion).

Missing, and needed (from the review):

- unclamped temperature;
- sea-surface temperature as a field;
- a pressure field;
- physical wind speed;
- a time axis;
- day length and year length;
- a Coriolis parameter exposed to consumers;
- humidity;
- maritime/continentality exposed (it is computed in `ComputeMaritimeAir`, then thrown away).

## 4. Architecture

```
            (generation, unchanged)                       (runtime, new, pure)
ClimateInfo + Elevation + Hydrology + Biome ──► Climatology ──► SynopticModel ──► WeatherField
         (annual means + descriptors)          (month m)        (storms, year y)    Sample(x, t)
                                                    │                 │                 │
                                                    └─ PressureModel ─┘                 ├─ Downscaler (region/local)
                                                       (SLP, geostrophic wind)          └─ Phenomena
```

New namespace `RoguelikeToolkit.World.Core.Weather`, layer rank above Climate. Everything hangs off
a lazily created `World.Weather` (`WeatherModel`), configured with `WorldBuilder.WithWeather(o => …)`.

### 4.1 Time and calendar

- `WorldTime` is a `long` of **planet hours since epoch**. Integer, total-ordered, serialisable.
- `PlanetCalendar` is derived from `PlanetParameters` plus new knobs:
  - `DayLengthHours` defaults to `24 / RotationRate`, which ties to the existing rotation knob.
  - `YearLengthDays` defaults to 365.25.
  - `Eccentricity` and `PerihelionDay` default to Earth (0.0167). This is optional, because the
    insolation formula assumes a circular orbit today.
- Solar declination: `δ(d) = asin(sin ε · sin(2π(d − d_equinox)/Y))`. Hour angle comes from longitude,
  so there is a diurnal cycle with local solar time.

### 4.2 W1: Climatology (month-resolved means)

`Climatology.At(tile, dayOfYear)` gives the expected state for that day of the year. It is analytic,
from existing fields:

- **Temperature.**
  - `T(d) = T̄ + A · cos(2π(d − d_peak)/Y)`, where `A = SeasonalRange`.
  - `d_peak` = summer solstice + lag. The lag runs from ~30 days (continental) to ~60 days
    (maritime) and is driven by maritime-ness.
  - The sign follows the hemisphere.
  - Tidally locked worlds: `A ≈ 0`, no lag.
- **Rain belts.** The ITCZ, subtropical high and storm track move with the sun: re-evaluate
  `ZonalFactor(|lat − k·δ(d)|)`, with `k ≈ 0.65`, matching `RainSeasonality`. This replaces the
  two-sample descriptor with the continuous curve that generated it. The construction makes the
  annual mean ≈ the stored `Precipitation`. Renormalise per tile so the 12-month mean equals
  `ClimateInfo.Precipitation` exactly.
- **Monsoon.** A land–sea thermal contrast term. In summer, a warm continental interior (high
  continentality × positive T anomaly) gives a thermal low, onshore flow and a rain boost on the
  windward coast. In winter it reverses: a cold high, offshore flow, dry. This is what the zonal model
  can't produce (South Asia, West Africa, northern Australia).
- **Units.** The `Physical` mapping is calibrated once and documented:
  - `T_C = 27 − 55·(1 − T_raw)`, which needs the **unclamped** T (see W0).
  - Precipitation is mapped to mm/yr monotonically, anchored to the biome thresholds: the desert
    threshold ↔ 250 mm, the forest threshold ↔ ~750 mm, 1.0 ↔ ~3500 mm.

Implementation: compute on the fly (cheap), or bake 12 monthly slices on demand. Size 7 costs
164k × 12 × ~16 B ≈ 31 MB. That is opt-in, because an interactive map scrubbing months wants it.

### 4.3 W2: Pressure and wind

A diagnostic sea-level pressure field `p(x, d)`:

1. **Zonal profile** shifted by `k·δ(d)`:
   - equatorial low (ITCZ) ≈ 1008 hPa;
   - subtropical highs at ~30°·bandScale ≈ 1020 hPa;
   - subpolar lows at ~60° ≈ 995 hPa;
   - polar highs.

   Use the same band positions as the wind cells, so the two can never disagree. This is the fix for
   the review's rotation-parity issue.
2. **Thermal term**: `−c · (T_land(d) − T_zonal(d))` over land, weighted by continentality. Warm
   continents in summer get thermal lows, cold ones in winter get highs. The Siberian High,
   Aleutian/Icelandic lows and monsoon lows emerge without being authored.
3. **Ocean term**: a small negative anomaly over warm currents (`OceanHeat > 0`), which strengthens
   the western-boundary storm genesis zones.
4. **Smoothing**: Laplacian-smooth on the tile graph (2–3 passes).

**Wind from pressure.** The gradient comes from a least-squares fit over neighbours in the tile's
tangent frame (the same fit as `FitGradient`, *with* the centring fix).

- Coriolis: `f = 2Ω sin φ`, `Ω = Ω_earth · RotationRate`.
- Geostrophic wind: `v_g = (1/ρf) k̂ × ∇p`.
- Friction turns it toward low pressure by `α` (≈20° ocean, ≈35° land, more over rough terrain from
  elevation variance) and damps it (×0.7 ocean, ×0.5 land).
- Near the equator, where `|f|` → 0, blend to down-gradient flow over |lat| < 8°. This avoids the
  singularity and gives the ITCZ convergence.
- Output: speed in m/s plus a direction. The magnitude is calibrated so the trades come out at
  ~6–8 m/s and the westerlies at ~8–12 m/s.

**Upper-level steering flow.** Storms need the mid-troposphere flow, not the surface wind. Use the
thermal wind: `v_500 ≈ v_sfc + (H/f)·k̂ × ∇T`. The meridional temperature gradient then produces the
mid-latitude jet, at its strongest in winter and displaced equatorward. The same quantity gives the
**vertical shear** `|v_500 − v_sfc|`, which tropical genesis needs.

**Tidally locked worlds.** Replace the zonal profile with a substellar-centred one: a low at the
substellar point, a high on the night side, flow day→night aloft and night→day at the surface. This
addresses the review's "tidal lock only affects temperature" finding for the weather layer.

### 4.4 W3: Synoptic systems (the storms)

Storms are **procedural events**, not simulated state.

- **Catalogue.** For each `(seed, year y)`, `StormCatalog.Generate(y)` deterministically produces
  every storm whose genesis falls in year `y`: its genesis time and place, and a full track sampled
  every 6 h.
- **Cost and caching.** Generating a year costs O(storms × steps): Earth-like is ~90 tropical plus
  ~600 extratropical systems, each with 20–40 steps. That is sub-millisecond to a few ms. The cache
  holds two to three years in an LRU. A storm that crosses New Year is found by also asking year
  `y−1` (lifetimes are under 3 weeks).
- **RNG.** `Rng.Derive(seed, "wx", y, basin, slot)` with counter-based streams (the existing
  `Rng`/`HistoryRng` style). A storm's track never depends on another storm's draws, so editing
  climate in one basin does not reshuffle storms in another.

#### Genesis fields (per month, per tile, computed once and cached)

**Tropical cyclone genesis potential.** A simplified Emanuel–Nolan GPI:

```
GPI = [SST > 26.5 °C] · (SST − 26.5)^1.5
    · clamp(|lat| − 5°, 0, …) taper to 0 by ~25–30°
    · humidity(P_month)
    · (1 + 0.1·V_shear)^-2
    · vorticity proxy (|f| near ITCZ edge)
```

- **SST** = ocean tile `T_C(d)`, which includes `OceanHeat`. Warm western boundaries (Gulf Stream,
  Kuroshio) therefore get the genesis; cold eastern boundaries (Humboldt, Benguela) suppress it. The
  South Atlantic-style "no hurricanes" basin emerges where SST and shear say so.
- There is **no genesis within ~5° of the equator** (`f ≈ 0`). This is a hard rule and a test.
- **Seasonality** comes from the SST lag, giving a late-summer peak.

**Extratropical cyclone genesis:**

- **Baroclinicity**: `|∇T|` at storm-track latitudes. It is strongest in winter.
- **Lee cyclogenesis**: downstream of ranges along the steering flow. Use `RangeCatalog` and
  elevation, giving Colorado or Genoa type lows.
- **Coastal**: east coasts over warm currents (`OceanHeat > 0`), giving Nor'easter or bomb-cyclone
  type lows.
- **Polar lows**: a small rate over open water next to sea ice in winter.

**Anticyclones and blocking**: a low-rate set of persistent highs (5–15 days) in the subtropical and
mid-latitude bands. They suppress precipitation and cause heatwaves or cold snaps.

**Sampling.** For each basin and month, compute `Λ = Σ GPI × area × rate`. Draw `N ~ Poisson(Λ)`,
then place each genesis with an alias table over the tile weights. Jitter in-tile and in time.

#### Tracks (6-hour fixed step, `DetMath`)

**Tropical cyclone:**

- **Motion**: `v = v_steer(500 hPa at the storm) + v_β`. The beta drift is ~2 m/s, poleward-west,
  and its sign follows the hemisphere.
- **Path shape**: trades carry the storm west. Near the subtropical ridge the steering weakens and
  the storm **recurves** poleward. In the westerlies it accelerates east and undergoes
  **extratropical transition**, becoming an ETC with an expanding wind field and an asymmetric rain
  shield.
- **Intensity**: track `V_max` and the central pressure deficit.
  - The storm grows toward the potential intensity `PI(SST)` at a rate cut by shear.
  - Over SST < 26 °C it decays exponentially.
  - Over land it uses Kaplan–DeMaria decay: `V(t) = V_b + (V_0 − V_b)·e^{−αt}`, α ≈ 0.095 h⁻¹. Rough
    terrain from elevation variance increases α.
  - Category is Saffir–Simpson.
- **Structure**: Holland (1980) wind profile from `p_c`, `R_max` and the B parameter, plus an
  asymmetry in which translation speed adds on the right of motion in the NH and on the left in the
  SH. Rain has an eyewall maximum, spiral bands and a total that scales inversely with translation
  speed (slow storms flood).
- **Hazards**:
  - **Storm surge** = f(onshore wind, shelf depth from ocean-floor elevation, which the library has,
    and coastline concavity).
  - **Inland flood index** = storm rain accumulated into the hydrology `Flow` network downstream.

**Extratropical cyclone:**

- **Motion**: moves with `0.8 · v_500`, with a slight poleward drift.
- **Lifecycle** (Norwegian model): deepen for 24–48 h, mature, occlude, fill. Lifetime 3–7 days.
- **Structure** in the storm-relative frame:
  - a **warm front** east of the low, along an arc, with a broad stratiform precipitation shield;
  - a **cold front** trailing equatorward-west, with a narrow convective band and a sharp T drop and
    wind veer when it passes;
  - an **occluded** comma head poleward.
- **Implementation**: fronts are parametric curves (Bézier in the tangent frame) rotated with the
  storm. A point's distance to each front drives that front's precipitation, temperature step and
  wind shift. Most real weather in mid-latitudes is this.

**Tracks are cheap to evaluate.** At time t a storm's state is a linear interpolation between two
6-h fixes. `StormsAt(t)` is a bucketed index (time bucket → active storms), and each storm also has
a bounding cap on the sphere, so a point query does O(nearby storms) work.

### 4.5 W4: Fair-weather variability (between storms)

Without this, the weather is climatology plus storms and feels clockwork. The approach:

- **Translated noise anomalies** (the "frozen turbulence" / Taylor hypothesis). Sample 4D fBm at
  `(x − v_steer·t, t/τ)`. The correlation length is ~1500 km and `τ` ~3–5 days. Anomaly patterns
  march west→east in the westerlies and east→west in the trades. `SphereNoise` is 3D today, so it
  needs a 4D simplex or a 3D + time-blend of two decorrelated fields.
- **Temperature anomaly** σ grows with continentality and latitude: ~1–2 K in the tropical ocean,
  ~6–8 K in Siberian winter. It is correlated negatively with cloud.
- **Precipitation** = climatology(d) × gamma-distributed multiplier with occurrence probability.
  - The mean is preserved: the weather's monthly mean must equal climatology's ±5%. This is a test.
  - Arid tiles get rare, intense events; humid tiles get frequent light rain.
  - Storm precipitation is *part of* the climatological budget, not added on top. The fair-weather
    multiplier is rescaled per tile and month to `climatology − E[storm contribution]`, with E
    estimated from the genesis fields. Otherwise storm tracks double-count rain.
- **Diurnal cycle**:
  - The temperature range is larger when dry, continental, high or clear.
  - The minimum is at sunrise and the maximum at ~15:00 local solar time.
  - Convective rain peaks in the afternoon over land and at night over the ocean.

### 4.6 W5: Phenomena and sample

```csharp
public struct WeatherSample
{
    public float TemperatureC, DewPointC, PressureHPa;
    public float WindEastMs, WindNorthMs, GustMs;
    public float PrecipitationMmPerHour, SnowDepthCm, CloudCover, VisibilityKm;
    public PrecipitationType Precipitation;   // None, Rain, Drizzle, Snow, Sleet, FreezingRain, Hail
    public WeatherPhenomena Phenomena;        // [Flags] Thunderstorm, Fog, Blizzard, DustStorm, Foehn,
                                              // LakeEffectSnow, TropicalCyclone, Gale, Heatwave, ColdSnap, Frost
    public int StormId;                       // -1 when not inside a named system
}
```

| Phenomenon | Rule |
|---|---|
| Precipitation type | wet-bulb from T and dew point; rain/snow line ~+1 °C, freezing rain under a warm-front inversion |
| Thunderstorm | CAPE proxy (surface T, dew point, lapse) × lift (cold front, afternoon heating, orography) |
| Fog | advection fog = moist air over a cold current (`OceanHeat < 0`) or on coasts downwind of one; radiation fog = clear, calm, humid night in valleys |
| Blizzard | snow + wind > 15 m/s |
| Dust storm | desert-family biome + wind > 10 m/s + dry month |
| Föhn / chinook | lee side of a range (elevation drop along the wind) with cross-range flow during cyclone passage: dry adiabatic warming of +5–15 K, humidity crash |
| Lake-effect snow | cold air (T_air ≪ T_water) crossing an unfrozen lake from `WaterBodyCatalog`; heavy snow on the downwind shore band |
| Snow depth | stateful in reality. Use a **degree-day estimator from climatology + the last N days of sampled weather** (N ≤ 60, sampled at 6 h, still deterministic and cache-friendly), not a running integral |

Snow depth and soil moisture are the only "memory" quantities. Both use bounded look-back windows,
so random access is preserved.

### 4.7 W6: Downscaling to region and local maps

`RegionHandle` and `LocalMapHandle` get `SampleWeather(cell, t)`:

- Interpolate the planet sample (barycentric over the 3 nearest planet tiles).
- Correct temperature with the lapse rate using the local height.
- Add orographic precipitation from the local slope · wind (windward ×, leeward ÷). This is the
  same physics as the generation stage, at the finer scale.
- Correct the wind for terrain channelling (valleys align the flow) and sheltering.
- Add cell-seeded noise for convective cell placement (a thunderstorm hits *this* valley, not that
  one).

This depends on the review's hierarchy fixes (`FitGradient`, unused `ParentContext.Wind`).

## 5. Public API (sketch)

```csharp
using var world = new WorldBuilder().WithSeed(42).WithSize(6)
    .WithPlanet(p => { p.ObliquityDegrees = 23.44; })
    .WithWeather(w => { w.Calendar = PlanetCalendar.Earth; w.StormRateScale = 1.0; w.BakeMonthlyClimatology = true; })
    .Build();

WeatherModel wx = world.Weather;
var t = wx.Calendar.ToTime(year: 12, dayOfYear: 250, hour: 14);

WeatherSample s = wx.Sample(tileIndex, t);
WeatherSample g = wx.Sample(new GeoCoord(25.8, -80.2), t);
wx.SampleAll(t, destination);                     // Span<WeatherSample>, planet snapshot, no allocation
MonthlyClimate m = wx.Climatology.At(tileIndex, month: 8);

IReadOnlyList<Storm> active = wx.StormsAt(t);
IReadOnlyList<Storm> season = wx.StormsInYear(12);
Storm s0 = season[0];  // Id, Kind (Tropical/Extratropical/PolarLow/Anticyclone), Name, Genesis, Lysis,
                       // Track: StormFix[] { Time, Position, CentralPressureHPa, MaxWindMs, RadiusMaxWindKm, Category }
                       // Landfalls: (tile, time, category)

// Hooks for games
wx.ForecastAt(tileIndex, from: t, hours: 72, stepHours: 6, Span<WeatherSample> dst);   // just samples of the future: it's a pure function
IWeatherModifier                                   // game-owned overrides (rain magic, curses) applied at sample time, like IHistoricalContext
```

`StormFix` and `WeatherSample` are blittable. `Storm.Name` is lazy, built from the name generator
with a per-basin culture (the "local" people's naming style, via `PeoplesStore` / `HistoryCatalog`
ownership at genesis). Storm names come from the world's own languages.

## 6. Integration points

- **History**:
  - `HazardOperator` gets a `CycloneHazard`. A landfall at Cat ≥3 within N tiles of a settlement
    raises damage or raze probability, and the coast gets a scar.
  - Weather years are deterministic, so "the Great Storm of year 212" is reproducible and can be
    cited by the myth/faith post-pass.
  - History turns are years, so the operator reads `StormsInYear(turn)`.
- **Map editor**:
  - Overlays: monthly climatology (T, P, SLP isobars, wind arrows via the existing `FlowArrows`),
    storm tracks for a year, and an animated snapshot with a time scrubber.
  - WASM exports: `WeatherSnapshot(t) → float[]` (packed T, P, u, v, cloud) and
    `StormTracks(year) → float[]`.
- **App (Avalonia)**: a "Weather" colour mode plus a storm-track line overlay. This is the first
  visual validation surface.
- **Unity**: a cloud and precipitation layer and storm sprites on the globe. See
  [UNITY_PLAN.md](UNITY_PLAN.md) §U6.

## 7. Phases

| Phase | Content | Acceptance |
|---|---|---|
| **W0 Prerequisites** | Review batch 1 (`DetMath.Exp`, PQ tie-break). Store **unclamped** temperature (new `ClimateInfo.TemperatureRaw`; classifiers keep reading the clamped value, so worlds don't change). Expose `Maritime` (continentality) and `SeaSurfaceAnomaly` as fields (append to `ClimateInfo`, bump the store header). Fix `FitGradient`. | Golden checksums move once (determinism fix) and are re-baselined. All biome histograms unchanged after the re-baseline. New fields covered by tests. |
| **W1 Calendar + climatology** | `WorldTime`, `PlanetCalendar` (day length from `RotationRate`), declination, `Climatology.At`, physical-unit mapping, monsoon term. | 12-month mean T and P equal `ClimateInfo` within 1e-6 (exact renormalisation). NH July warmer than January at 45°N inland, inverse in the SH. Amplitude ranks continental > maritime. A monsoon coast shows a summer P peak ≥3× winter. Zero tilt gives zero seasonality. |
| **W2 Pressure + wind** | SLP field, geostrophic + friction wind, equatorial blend, thermal-wind steering and shear, tidally locked variant. | Sign tests: NH lows rotate counter-clockwise, SH clockwise. Trades are NE in the NH and SE in the SH. Westerlies 35–60°. A winter continental high appears on a large NH continent. A size-4 to size-6 change keeps band positions within one tile. No NaN at the poles or equator. |
| **W3 Storms** | Genesis fields, the per-year catalogue, TC and ETC tracks, intensity, Holland profile, fronts, LRU cache, `StormsAt` index. | No TC genesis within 5° of the equator (hard). TCs recurve: ≥70% of long-lived NH TCs end NE of genesis. TC landfall decay matches K–D within tolerance. ETC genesis density peaks in the storm-track band and in lee/coastal zones. A Cat 3+ never persists >36 h over land. `StormsInYear` is identical regardless of query order or cache state. Determinism hash is identical on net10 and netstandard2.1. |
| **W4 Variability + diurnal** | 4D noise (or a 3D + time blend), translated anomalies, gamma precipitation with storm-budget rescaling, diurnal cycle. | Monthly mean of sampled P = climatology ±5% over 30 simulated years (statistical test, fixed seed). Anomaly autocorrelation e-folds within 3–5 days. Mid-latitude anomalies propagate eastward (lag-correlation test). |
| **W5 Phenomena + sample API** | `WeatherSample`, precipitation type, phenomena flags, bounded-look-back snow depth, `SampleAll`, `ForecastAt`, `IWeatherModifier`. | Each phenomenon fires in its archetype location and not in its counter-archetype: fog off cold currents but not off warm ones, föhn on the lee side only, lake-effect snow on downwind shores only, dust storms only in arid biomes. `SampleAll` at size 6 under 10 ms single-threaded, zero allocations after warm-up (benchmark project). |
| **W6 Downscaling** | Region/local `SampleWeather`. | Local lapse-rate consistency. Windward local cells wetter than leeward under the same planet sample. Continuity across region borders (with the hierarchy tiling fix). |
| **W7 Consumers** | App weather mode, editor overlays and scrubber, history `CycloneHazard`, storm naming. | Visual review checklist. A history corpus re-baseline with the hazard enabled only behind a flag. |

## 8. Later: Climate v2 (opt-in, changes worlds)

Once W2 exists, the generation `ClimateStage` could take its wind from the pressure model rather than
the step-function cells. That fixes the review's wind and rotation findings properly. Annual
precipitation could also come from integrating W1–W4 statistics (moisture advected along the real
wind, depleted by rain-out) instead of the maritime-reach heuristic. This changes every world, so it
goes behind `WithClimateModel(ClimateModelKind.V2)` and its own golden corpus, and never becomes a
silent default.

## 9. Risks

- **Double-counting rain** (storms on top of climatology). Mitigated by the budget rescaling in W4 and
  its statistical test.
- **Noise-driven "weather" that doesn't move.** The translated-noise design and the propagation test
  exist for this.
- **Cost of the genesis fields at size 7.** 12 months × 164k tiles, computed once per world and
  lazily. Fine, but measure in the benchmark project.
- **Physical-unit calibration bikeshedding.** The mapping is one documented table. Games that want
  different numbers override it via `WeatherOptions.Units`.
- **4D noise on netstandard / IL2CPP.** It must be `DetMath`-only, integer-hashed gradients, no SIMD
  paths that differ by platform.
