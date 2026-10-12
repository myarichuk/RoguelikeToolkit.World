using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Dense per-tile climate field. Temperature is ~[0..1] (0 polar, 1 equatorial
/// before lapse-rate correction), Precipitation is ~[0..1], wind is a unit-ish
/// 3D vector stored as components to stay blittable.
/// </summary>
public struct ClimateInfo
{
    public float Temperature;
    public float Precipitation;
    public float WindX;
    public float WindY;
    public float WindZ;
    /// <summary>Half the annual swing of Temperature (same units), larger inland and towards the poles; see <see cref="ClimateModel.SeasonalAmplitude"/>.</summary>
    public float SeasonalRange;
    /// <summary>How unevenly the year's rain falls, 0 (even) .. 1 (monsoon / Mediterranean); see <see cref="ClimateModel.RainSeasonality"/>.</summary>
    public float RainSeasonality;
}

public class ClimateLayer : StoreLayer<ClimateInfo>
{
    public ClimateLayer(WorldDataStore store) : base(store) { }
}
