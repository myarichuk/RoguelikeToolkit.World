// Land use from capacity: farm the best ground, fence pasture on good grass that is not farmed, and
// let the rest be hunted or fished. Runs after the land-capacity stage (25) and before the pin
// resolver (99), so a hand-painted tile always wins.
// Declare Reads: LandCapacityInfo, LandUseInfo; Writes: LandUseInfo (a later writer must also read it).

function execute() {
    var farmAbove = PARAMS.farmAbove === undefined ? 128 : PARAMS.farmAbove;
    var pastureAbove = PARAMS.pastureAbove === undefined ? 128 : PARAMS.pastureAbove;
    for (var i = 0; i < TILE_COUNT; i++) {
        if (arable(i) >= farmAbove) {
            setLandUse(i, LANDUSE.Farmland);
            setIntensity(i, arable(i));
        } else if (pasture(i) >= pastureAbove) {
            setLandUse(i, LANDUSE.Pasture);
            setIntensity(i, pasture(i));
        }
        var act = 0;
        if (game(i) >= 128) act |= ACTIVITY.Hunting;
        if (timber(i) >= 128) act |= ACTIVITY.Logging;
        if (fish(i) >= 64) act |= ACTIVITY.Fishing;
        if (act !== 0) setActivities(i, act);
    }
}
