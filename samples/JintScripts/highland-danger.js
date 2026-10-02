// Highland danger rebalance: mountain biomes get +1 danger, ocean stays 0.
// Runs after biomes (order 21). Reads LocalMapInfo, refines LocalMapInfo.

function execute() {
    for (var i = 0; i < TILE_COUNT; i++) {
        var b = biome(i);
        if (b === BIOME.Mountain) {
            setDanger(i, danger(i) + 1);
        } else if (b === BIOME.Ocean) {
            setDanger(i, 0);
        }
    }
}
