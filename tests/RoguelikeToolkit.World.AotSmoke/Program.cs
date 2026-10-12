using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.Entities;
using RoguelikeToolkit.World.Core.Names;

var failures = new List<string>();
void Check(bool ok, string what)
{
    if (!ok) failures.Add(what);
}

ulong hash = 14695981039346656037ul;
void Mix(ulong v)
{
    for (int i = 0; i < 8; i++)
    {
        hash ^= (v >> (i * 8)) & 0xFF;
        hash *= 1099511628211ul;
    }
}

// 1. Full default generation (tectonics, elevation, climate, erosion, hydrology, biomes) + catalogs.
using (var world = new WorldBuilder().WithSize(3).WithSeed(42).Build())
{
    var heights = world.Map.DataStore.GetSpan<ElevationInfo>();
    var biomes = world.Map.DataStore.GetSpan<LocalMapInfo>();
    Check(heights.Length == world.TileCount && heights.Length > 0, "elevation layer");
    for (int i = 0; i < heights.Length; i++)
    {
        Mix((ulong)BitConverter.SingleToInt32Bits(heights[i].Height));
        Mix((ulong)biomes[i].Biome | ((ulong)biomes[i].DangerLevel << 8));
    }

    var vienna = new GeoCoord(48.2, 16.4);
    Mix((ulong)BitConverter.SingleToInt32Bits(world.SampleElevation(vienna)));
    Check(world.QueryRadius(vienna, 800).Count > 0, "radius query");
    Check(world.ScoreCitySites(new CitySiteFilter { TopN = 5 }).Count > 0, "city sites");
    _ = world.NearestWater(vienna);
    _ = world.NearestDeposit(vienna);
    Mix((ulong)world.Rivers.Rivers.Count);
    Mix((ulong)world.WaterBodies.Bodies.Count);

    // Ocean currents (stream-function hierarchy, heat relaxation): the climate layer carries their heat and flow.
    var climateLayer = world.Map.DataStore.GetSpan<ClimateInfo>();
    double currentHeat = 0;
    for (int i = 0; i < climateLayer.Length; i++)
    {
        Mix((ulong)BitConverter.SingleToInt32Bits(climateLayer[i].OceanHeat));
        Mix((ulong)BitConverter.SingleToInt32Bits(climateLayer[i].CurrentX));
        currentHeat += Math.Abs(climateLayer[i].OceanHeat);
    }
    Check(currentHeat > 0, "ocean currents warmed or cooled some coast");

    // 2. Lazy zoom: region -> local.
    var region = world.GetRegion(world.Resolve(vienna).TileIndex);
    var local = region.GetLocal(0);
    Check(region.Cells.Length > 0 && local.Tiles.Length > 0, "region/local zoom");
    Mix((ulong)region.Cells.Length);
    Mix((ulong)local.Tiles.Length);
}

// 2b. Planet parameters (tilt, flux, spin, custom insolation) and hand edits re-derived by Recompute: the
// climate refresh pass, meridional wind, ClimateModel and the edit layer all run under the AOT compiler too.
foreach (var planet in new[]
{
    new PlanetParameters { ObliquityDegrees = 60, StellarFlux = 1.1, RotationRate = 2 },
    new PlanetParameters { Insolation = new TidallyLockedInsolation(new Vector3D(1, 0, 0)) },
})
{
    using var tilted = new WorldBuilder().WithSize(3).WithSeed(7).WithPlanet(planet).WithUserEdits().Build();
    var tClimate = tilted.Map.DataStore.GetSpan<ClimateInfo>();
    for (int i = 0; i < tClimate.Length; i++)
    {
        Mix((ulong)BitConverter.SingleToInt32Bits(tClimate[i].Temperature));
        Mix((ulong)BitConverter.SingleToInt32Bits(tClimate[i].RainSeasonality));
        Mix((ulong)BitConverter.SingleToInt32Bits(tClimate[i].RainPhase));
    }
    Check(tilted.Editable, "editable world");
    tilted.Edits[tilted.TileCount / 2] = new UserEditInfo { HeightDelta = 0.2f };
    tilted.Edits[tilted.TileCount / 3] = new UserEditInfo { TemperatureDelta = 0.15f, PrecipitationDelta = -0.1f };
    tilted.Recompute();
    var tClimate2 = tilted.Map.DataStore.GetSpan<ClimateInfo>();
    Mix((ulong)BitConverter.SingleToInt32Bits(tClimate2[tilted.TileCount / 3].Temperature));
    Mix((ulong)BitConverter.SingleToInt32Bits(tClimate2[tilted.TileCount / 3].Precipitation));
    var tHeights = tilted.Map.DataStore.GetSpan<ElevationInfo>();
    Mix((ulong)BitConverter.SingleToInt32Bits(tHeights[tilted.TileCount / 2].Height));
    Mix((ulong)tilted.Rivers.Rivers.Count);
}

// 3. Pipeline discovery (reflection over Core's stage types) + contract validation.
using (var pipeline = new WorldGenerationPipeline())
{
    pipeline.DiscoverFrom(Array.Empty<System.Reflection.Assembly>());
    Check(pipeline.Stages.Count >= 5, "stage discovery");
    pipeline.ValidateContracts();
}

// 4. Names (embedded YAML tables).
var name = FantasyNameGenerator.GeneratePerson(FantasyRaces.Dwarf, Cultures.FantasyCommon, seed: 42);
Check(!string.IsNullOrWhiteSpace(name.Text), "person name");
foreach (var c in name.Text) Mix(c);

// 5. Entity store: spawn, snapshot, reload.
var store = new EntityStore();
var places = store.AddColumn<int>();
var ids = new List<EntityId>();
for (int i = 0; i < 5000; i++)
{
    var id = store.Create();
    places[id] = i * 7;
    ids.Add(id);
}
store.Destroy(ids[10]);
using (var ms = new MemoryStream())
{
    store.Save(ms);
    ms.Position = 0;
    var reloaded = new EntityStore();
    var reloadedPlaces = reloaded.AddColumn<int>();
    reloaded.Load(ms);
    Check(reloaded.Count == 4999 && reloadedPlaces[ids[20]] == 140 && !reloaded.IsAlive(ids[10]), "entity snapshot round-trip");
}

Console.WriteLine($"checksum {hash:X16}");
if (failures.Count > 0)
{
    Console.WriteLine("AOT SMOKE FAILED: " + string.Join("; ", failures));
    return 1;
}

Console.WriteLine("AOT SMOKE OK");
return 0;
