// One or two sentences per term the editor shows, used for tooltips. Plain language first, the model
// behind it second; units are the engine's (temperature 0..1 is about 55 K; relief ~±1.2, sea level 0).

/** Inspector fact labels (see engine/world/features.ts). */
export const FACT_HINTS: Record<string, string> = {
  Area: 'Land or water surface covered by the feature, summed over its tiles.',
  Climate: 'Annual-mean temperature and rainfall of the feature, in words. Temperature falls with latitude and altitude; rain follows the wind belts, mountains and the sea.',
  Biomes: 'Share of the feature covered by each biome.',
  Deposits: 'Resource deposits inside the feature.',
  Elevation: 'Surface height above sea level (sea level is 0, the highest peaks are about 1.2).',
  Biome: 'Land cover chosen from temperature, rainfall, height and drainage (or pinned by hand).',
  Plate: 'The tectonic plate under this tile. Oceanic crust is thin and low, continental crust thick and high; boundaries raise mountains and trenches.',
  Culture: 'The naming tradition used for this feature: each tectonic plate is named by one culture.',
  Length: 'River length along its tile chain, stretched 25% because tile chains cut meanders short.',
  'Discharge at mouth': 'Water carried at the mouth relative to the source: how many times bigger the river has grown by collecting tributaries and rain.',
  'Flows into': 'Where the river ends: the sea, a lake, or an inland sink where it evaporates.',
  'Source elevation': 'Height of the river\'s first tile.',
  Type: 'The feature\'s subtype.',
  Rank: 'Settlement size class, decided by site quality and spacing.',
  'Site score': 'How good the place is for a settlement: fresh water, flat fertile ground, climate and nearby resources.',
  'Water access': 'How a settlement reaches fresh water (river, lake, coast or none).',
  'Nearby resources': 'Deposits within the settlement\'s reach.',
  Resource: 'Kind of ore, fuel or mineral. Deposits form where the geology allows them (e.g. oil in basins, gold near volcanic arcs).',
  Richness: 'Quantity and grade of the deposit, from poor to rich.',
};

export const PLANET_HINTS = {
  obliquity: 'Axial tilt: the angle between the spin axis and the orbit. More tilt means stronger seasons and a smaller gap between pole and equator. Earth is 23.4°; 0° has no seasons, above 54° the poles are warmer than the equator on average.',
  flux: 'Stellar flux: how much starlight the planet receives relative to Earth. A few percent moves the whole climate; 10% turns Earth\'s temperate zones hot or cold.',
  rotation: 'Spin rate relative to Earth. Faster spin makes narrower wind belts and more, thinner climate bands; slower spin makes fewer, wider ones.',
} as const;

export const WORLD_HINTS = {
  seed: 'Same seed, same size and same planet make the same world every time.',
  detail: 'Resolution of the simulated planet (an icosphere of hexagonal tiles). Each step has four times the tiles: finer rivers and coasts, slower generation.',
  plates: 'Number of tectonic plates. More plates make smaller continents and more mountain belts; fewer give broad continents.',
  density: 'How many decals (trees, hills, mountains) are scattered over the map.',
  warp: 'How far coastlines are distorted by noise, in map units, to look hand-drawn rather than hexagonal.',
  detailNoise: 'Fine-scale roughness added to coast and relief edges.',
  ripple: 'Distance between the decorative coastline ripples in the water.',
  cultures: 'Naming traditions. Each plate is assigned one, and its mountains, rivers and towns are named in it.',
} as const;

export const LAYER_HINTS: Record<string, string> = {
  hillshade: 'Shading as if the sun lit the terrain from the north-west, so slopes read as relief.',
  contours: 'Lines of equal height.',
  coastline: 'Outline where land meets sea.',
  ripples: 'Decorative rings in the water along the coast.',
  rivers: 'River ribbons; width follows discharge.',
  stamps: 'Hand-drawn style decals: mountains, trees, hills.',
  labels: 'Names of places and geographic features.',
  resources: 'Ore, fuel and mineral deposit markers. Shown only when the Resources feature kind is on.',
  plateBoundaries: 'Edges between tectonic plates, where mountains, trenches and volcanoes form.',
  plateMotion: 'Arrows for each plate\'s drift direction; length grows with speed.',
  graticule: 'Latitude and longitude lines.',
  capacity: 'Tints the land by how much it could carry of the product chosen below (farming, grazing, game, timber or fish). It is what the climate and terrain allow, not what anyone has built. Needs a world made by the engine; Apply physics refreshes it after you edit.',
  climate: 'Colours the whole map by one climate quantity from the engine (temperature, rainfall, ocean currents or the size of the seasons). Pick which one below. It shows what the physics produced, including your climate paint after Apply physics.',
  wind: 'Arrows for the prevailing wind. Longer means stronger. Winds carry rain inland from the sea and cause rain shadows behind mountains.',
  currents: 'Arrows for the sea currents. Red ones carry warm water and warm the coast they touch; blue ones carry cold water and cool and dry it.',
  climatePaint: 'Tint of the temperature and rainfall offsets painted with the Climate tool (also shown while that tool is active).',
};

export const BASE_HINTS: Record<string, string> = {
  styled: 'The finished look: theme colours, biomes softened and shaded.',
  biomes: 'Flat biome colours, as the simulation classified them.',
  elevation: 'Height as a colour ramp: deep sea to high peaks.',
  plates: 'Tectonic plates, one colour each.',
};

export const TERRAIN_HINTS: Record<string, string> = {
  raise: 'Adds height under the brush. Alt lowers.',
  lower: 'Removes height under the brush. Alt raises.',
  smooth: 'Averages neighbouring heights, softening ridges and pits.',
  flatten: 'Pulls the ground toward the height at the start of the stroke: terraces, mesas.',
  roughen: 'Adds random relief, for broken, craggy ground.',
  land: 'Lifts the sea floor just above sea level.',
  sea: 'Sinks the ground just below sea level.',
};
