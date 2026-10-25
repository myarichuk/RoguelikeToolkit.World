using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Entities;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Names;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;

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
    var biomes = world.Map.DataStore.GetSpan<BiomeInfo>();
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

// 4b. Peoples (embedded folk/culture YAML + compat rules load under trimming).
var peoples = RoguelikeToolkit.World.Core.Peoples.PeoplesStore.Load();
var dwarf = peoples.GetFolk("dwarf");
Check(dwarf.MaturationYears == 50, "folk table");
Check(!peoples.Compatibility.TryUnion("demon", "angel", out _), "compat table");
foreach (var c in dwarf.Key + peoples.GetCulture("dwarven_clan").NamingTable) Mix(c);
// Court and epithet tables (nested dictionaries and lists of DTOs), then a history with named persons on them.
Check(peoples.Courts.TryGetValue("default", out var court) && court.Forms.Count == 4 && court.Skills.Count == 4, "court table");
Check(peoples.TraitTables.TryGetValue("epithets", out var epithets) && epithets.Epithets.Count > 0 && epithets.Epithets[0].Min.Count > 0, "epithet table");
// Tactics, ways of war and the battle model (dictionaries of DTOs, lists of DTOs), then a history with persons and battles.
Check(peoples.Tactics.TryGetValue("ambush", out var ambush) && ambush.Initiative && ambush.TerrainFit.Count > 0, "tactic table");
Check(peoples.Warfare.TryGetValue("orcish_horde", out var horde) && horde.Units.Count > 0 && horde.Doctrine.Count > 0 && horde.Customs.Count > 0, "warfare table");
Check(peoples.Battles.TryGetValue("default", out var battle) && battle.Beats.Count > 0 && battle.Epithets.Count > 0 && battle.Customs.Count > 0
    && battle.BiomeTerrain.Count > 0 && battle.Aftermath.Count > 0, "battle table");
// Walls, forts and sieges (a dictionary of fort-kind DTOs), then the history runs with them on.
Check(peoples.Fortifications.TryGetValue("default", out var fortification) && fortification.Kinds.Count > 0 && fortification.TierWalls.Count > 0, "fortification table");
Check(peoples.Sieges.TryGetValue("default", out var siegeTable) && siegeTable.GarrisonShare.Count > 0 && siegeTable.FoodSeason.Count > 0, "siege table");
Check(horde.Fortify < peoples.Warfare["dwarven_clan"].Fortify && !string.IsNullOrEmpty(peoples.GetCulture("dwarven_clan").Architecture), "fortify and architecture");
using (var courtly = new WorldBuilder().WithSize(3).WithSeed(7)
           .WithHistory(c => c.Features = RoguelikeToolkit.World.Core.History.HistoryFeatures.Persons
               | RoguelikeToolkit.World.Core.History.HistoryFeatures.Battles | RoguelikeToolkit.World.Core.History.HistoryFeatures.Sieges).Build())
{
    Check(courtly.History.Persons.Count > 0 && courtly.History.Tenures.Count > 0, "court persons");
    Check(courtly.History.Battles.Count > 0, "battles");
    foreach (var p in courtly.History.Persons) { Mix((ulong)(uint)p.StableKey); Mix(p.Martial); Mix((ulong)(uint)p.DiedTurn); }
    foreach (var b in courtly.History.Battles)
    {
        Mix((ulong)b.Outcome); Mix((ulong)(uint)b.Tile); Mix((ulong)(uint)b.Attacker.Tactic); Mix((ulong)b.Kind); Mix((ulong)(uint)b.SiegeMonths);
        Mix((ulong)(long)(b.Attacker.LossShare * 1e9)); Mix((ulong)(long)(b.Defender.Strength * 1e3));
    }
}

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
