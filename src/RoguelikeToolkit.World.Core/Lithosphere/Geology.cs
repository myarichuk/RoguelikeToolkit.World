using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>Derives the <see cref="CrustalProvince"/> and basement age of a tile from the tectonic layer.</summary>
public static class Geology
{
    /// <summary>Basement older than this (on a 0..1 scale) is cratonised if it is also interior and quiet.</summary>
    public const float CratonMinAge = 0.55f;

    /// <summary>Boundary rings from which continental crust counts as interior (the tectonic belt width is ~4 rings).</summary>
    public const int InteriorRings = 3;

    /// <summary>Orogeny magnitude below which a tile is undeformed.</summary>
    public const float QuietOrogeny = 0.05f;

    /// <summary>
    /// Basement age on 0..1 (0 = young, 1 = Archaean). Mostly a property of the plate
    /// it sits on, the way old continental nuclei are, blended with low-frequency noise so
    /// a plate is not uniformly ancient. Pure function of (seed, plate, position).
    /// </summary>
    public static float BasementAge(int seed, ushort plateId, Vector3D position)
    {
        double plate = Rng.Create(seed + 7001, plateId).NextDouble();
        double patch = SphereNoise.Fbm(position * 1.6, seed + 7003) * 0.5 + 0.5;
        return (float)Math.Clamp(plate * 0.62 + patch * 0.38, 0.0, 1.0);
    }

    /// <summary>Province of a tile from its tectonic state and basement age.</summary>
    public static CrustalProvince Classify(in TectonicPlate plate, float age)
    {
        if (plate.Crust == CrustType.Oceanic)
        {
            return plate.NearestBoundary switch
            {
                PlateBoundaryType.Divergent => CrustalProvince.SpreadingRidge,
                PlateBoundaryType.Convergent => CrustalProvince.Subduction,
                _ => CrustalProvince.OceanBasin,
            };
        }
        if (plate.NearestBoundary == PlateBoundaryType.Convergent && plate.Orogeny > QuietOrogeny)
            return CrustalProvince.Orogen;
        if (plate.NearestBoundary == PlateBoundaryType.Divergent && plate.Orogeny < 0f && plate.BoundaryRings <= 2)
            return CrustalProvince.Rift;
        bool interior = plate.BoundaryRings >= InteriorRings;
        if (interior && Math.Abs(plate.Orogeny) < QuietOrogeny && age >= CratonMinAge)
            return CrustalProvince.Craton;
        return CrustalProvince.Platform;
    }
}
