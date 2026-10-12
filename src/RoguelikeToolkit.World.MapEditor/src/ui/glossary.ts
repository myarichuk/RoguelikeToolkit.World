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
  'Site score': 'How good the place is for a settlement: fresh water, flat fertile ground, climate and nearby resources. A coast the sea freezes over in winter scores a little lower, and one locked in permanent ice has no water access.',
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

export const MODEL_HINTS = {
  climateRefresh: 'After erosion reshapes the land, work out the climate again on the new relief, so rain shadows and biome edges follow the mountains you see. Turn it off for the older single-pass climate (drier coasts, fewer inland rivers).',
  meridionalWind: 'How much the wind blows north or south as well as east or west, as the Earth\'s spin turns it. 0 gives purely east-west winds; the default 0.3 gives the diagonal trade winds and westerlies, and slants rain shadows.',
  windCellBlend: 'Lets the wind die down at the edges of each wind belt (the doldrums and horse latitudes) instead of reversing at full strength. Calm belts lift and shade less, so the world gets a little drier with slightly more desert. 0 keeps the classic abrupt reversal.',
  oceanHeat: 'How strongly warm and cold sea currents warm or cool the coasts they touch (the Gulf Stream, the Humboldt). 0 switches the temperature effect off; 2 doubles it.',
  oceanRain: 'How strongly those currents change coastal rain: cold currents make fog deserts, warm ones wet coasts. 0 switches it off.',
  aeolian: 'Passes of wind erosion over dry land: it scours basins upwind and piles sand downwind into sand seas. 0 switches it off; more passes make bigger features.',
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
  climate: 'Colours the whole map by one climate quantity from the engine (temperature, rainfall, ocean currents, the size of the seasons, which season the rain falls in, or where the sea freezes). Pick which one below. It shows what the physics produced, including your climate paint after Apply physics.',
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

/** Theme editor controls, keyed by the `MapStyle` field they write. */
export const THEME_HINTS: Record<string, string> = {
  paper: 'Background colour of the map sheet, behind everything.',
  sea: 'Colour of deep water.',
  shallow: 'Colour of shallow water along the coast.',
  seaInk: 'Ink for ripples and other marks drawn on the water.',
  landInk: 'Ink for outlines and marks drawn on the land.',
  river: 'Colour of rivers.',
  frame: 'Colour of the border drawn around the map.',
  coastWidth: 'Thickness of the coastline.',
  haloWidth: 'How far the lighter shallow-water band reaches out from the coast.',
  rippleCount: 'How many decorative rings run along the coast.',
  rippleOpacity: 'How strong the coastal rings are; 0 hides them.',
  hillshade: 'How strongly slopes are shaded. 0 is flat, 1 is full relief shading.',
  grain: 'Amount of paper texture speckle.',
  stains: 'Amount of blotchy ageing on the paper.',
  innerCoast: 'Darkening of the land just inside the coastline.',
  riverWidth: 'Overall thickness of river ribbons.',
  name: 'The theme\'s name, as shown in the Theme picker.',
  biomes: 'Fill colour of each biome in the styled map.',
  inks: 'Colours the hand-drawn stamps (mountains, trees, hills) are inked with. Changing one redraws the stamps.',
  labelKind: 'Which sort of label to style: towns, rivers, mountains and so on. The controls below change only this kind.',
  labelFont: 'Letter style for this kind of label: serif, italic or capitals.',
  labelSize: 'Base text size of this label kind.',
  labelGrow: 'How much bigger features get bigger labels. 0 makes every label the same size.',
  labelTracking: 'Extra space between letters. Wide spacing suits large areas such as seas and ranges.',
  labelHalo: 'Thickness of the outline that keeps the text readable over the map.',
  labelMinZoom: 'Zoom level at which this kind of label appears; "always" shows it at every zoom.',
  labelUppercase: 'Writes this kind of label in capitals.',
  labelFill: 'Text colour.',
  labelHaloColor: 'Colour of the outline around the text.',
  chrome: 'Colour of the editor\'s own panels and buttons (not the map).',
};

/**
 * Biome swatches whose cause is not obvious from the name; the rest show just their name. Keyed by the biome's pack name.
 * What grows them is the climate, so painting one is a statement that the climate there is what it is.
 */
export const BIOME_HINTS: Record<string, string> = {
  Savanna: 'Warm grassland with a dry season: hot, not wet enough for forest, and with its rain in summer (the Wet season layer shows brown). Painting it pins the biome there.',
  Mediterranean: 'Warm-temperate scrub and open woodland with a dry summer: the rain comes in winter (the Wet season layer shows teal) and is too little for forest. Good farmland, with olives and vines. Painting it pins the biome there.',
};

/**
 * Land use: what people (or fire) did to the ground, as against what the climate grows on it. It is a decision, not
 * physics: no climate, erosion or river stage reads it. Apply physics only hands it to the engine as a pin, so games
 * and plugins that decide land use keep what you painted.
 */
export const LANDUSE_HINTS = {
  tool: 'Paint what people or fire did to the ground (farmland, ashlands, pasture, built-up) over the biome. The biome underneath, the climate and the rivers do not change. Alt+click picks what is under the pointer.',
  clear: 'Removes land use under the brush; the biome underneath shows again.',
  note: 'Land use is drawn over the biome and never changes it: the climate, rivers and what the land could carry stay as they were. Apply physics hands your strokes to the engine as pins, so games and plugins that decide land use keep them; Revert keeps them too.',
} as const;

/** Top bar, status bar and content-pack controls. */
export const SHELL_HINTS = {
  theme: 'The map\'s look: colours, label styles and stamp inks. Edit the chosen theme in the Theme panel.',
  cursor: 'Under the pointer: latitude and longitude, height above sea level (sea is 0), biome (with the land use painted over it, in brackets), the named feature, the climate while a climate layer is on, and the map scale.',
  timings: 'How long the last Generate took, split into the world simulation, drawing the map, finding features and naming them. Hover for the exact numbers.',
  packImport: 'Add a content pack (themes, biomes, stamps, fonts) from a file on your computer.',
  packExample: 'Load the example pack that ships with the editor.',
  packToggle: 'Turn this pack on or off without removing it.',
  packExport: 'Save your own edits as a pack file you can share.',
} as const;
