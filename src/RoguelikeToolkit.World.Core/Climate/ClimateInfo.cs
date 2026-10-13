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
    /// <summary>Which season the rain favours, +1 winter (Mediterranean) .. -1 summer (monsoon); its magnitude is <see cref="RainSeasonality"/>. See <see cref="ClimateModel.RainPhase"/>.</summary>
    public float RainPhase;
    /// <summary>
    /// The temperature the ocean currents added (this tile's, in the same units as Temperature, already included in it):
    /// the sea-surface anomaly over water, the share of it a sea breeze carries onto coastal land. Positive
    /// beside a warm current (the Gulf Stream), negative beside a cold one (Humboldt, Benguela); see <see cref="OceanCurrents"/>.
    /// </summary>
    public float OceanHeat;
    /// <summary>Surface current over water (tangent to the sphere), in units of a strong boundary current; zero on land.</summary>
    public float CurrentX;
    public float CurrentY;
    public float CurrentZ;
}
