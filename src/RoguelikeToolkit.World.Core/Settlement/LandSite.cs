using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>
/// The facts about one tile the capacity model needs. A plain value so plugins, tests and the editor can
/// score a hypothetical tile without a world.
/// </summary>
/// <param name="Biome">The tile's final biome.</param>
/// <param name="Height">Surface height (sea level is 0).</param>
/// <param name="Temperature">Annual-mean <see cref="ClimateInfo.Temperature"/>.</param>
/// <param name="Precipitation"><see cref="ClimateInfo.Precipitation"/>.</param>
/// <param name="Slope">Steepest height change to a neighbour per unit of arc on the unit sphere. A gradient, not a step, so it means the same thing at every world size (a raw step shrinks as tiles get smaller).</param>
/// <param name="FreshWater">A river channel or lake on this tile or a neighbour: irrigation water within reach.</param>
/// <param name="OnFreshWater">This tile itself is a lake or carries a river (a fishable surface).</param>
/// <param name="Saline">The ground holds salt.</param>
/// <param name="OceanHeat">The ocean-current anomaly (<see cref="ClimateInfo.OceanHeat"/>); negative beside a cold current.</param>
/// <param name="NextToLand">A neighbour is land, so an ocean tile is on the coast.</param>
/// <param name="SeaIce">How much of the year the (ocean) tile is frozen over; see <see cref="SeaIce"/>.</param>
public readonly record struct LandSite(
    BiomeType Biome,
    double Height,
    double Temperature,
    double Precipitation,
    double Slope,
    bool FreshWater,
    bool OnFreshWater,
    bool Saline,
    double OceanHeat,
    bool NextToLand,
    SeaIceKind SeaIce = SeaIceKind.None);
