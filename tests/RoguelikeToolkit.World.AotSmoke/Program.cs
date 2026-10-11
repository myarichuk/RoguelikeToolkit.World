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

    // 2. Lazy zoom: region -> local.
    var region = world.GetRegion(world.Resolve(vienna).TileIndex);
    var local = region.GetLocal(0);
    Check(region.Cells.Length > 0 && local.Tiles.Length > 0, "region/local zoom");
    Mix((ulong)region.Cells.Length);
    Mix((ulong)local.Tiles.Length);
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
