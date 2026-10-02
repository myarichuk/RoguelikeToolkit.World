// Aridity tweak: dry the climate out after hydrology (order 19), before
// biomes (20). Same role as samples/DesertPlugin, written as untrusted JS.
//
// Host wiring (C#):
//   pipeline.AddStage(new JintWorldStage(new JintStageSpec {
//       Name = "aridity-js", Order = 19, Source = File.ReadAllText("aridity.js"),
//       Reads = new[] { typeof(ClimateInfo) },
//       Writes = new[] { typeof(ClimateInfo) },
//       Params = new Dictionary<string, object?> { ["dryness"] = 0.5 },
//   }));
//
// Available globals: TILE_COUNT, SEED, PARAMS, BIOME.{...},
//   lat(i), lon(i), continentality(i), orogeny(i), height(i),
//   temp(i), precip(i), flow(i), surface(i), biome(i), danger(i),
//   setHeight/setTemp/setPrecip/setBiome/setDanger(i, v), rand01(tile, salt).
// Reads go through declared Reads/ReadsOptional; setters require Writes.

function execute() {
    var dryness = PARAMS.dryness !== undefined ? PARAMS.dryness : 0.5;
    for (var i = 0; i < TILE_COUNT; i++) {
        var jitter = rand01(i, 77) * 0.1;
        setPrecip(i, precip(i) * dryness + jitter * 0.05);
    }
}
