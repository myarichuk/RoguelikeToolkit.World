using System;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(15)]
public class ElevationGenerationStage : IWorldGeneratorStage, ISeededStage
{
    public const double SeaLevel = 0.0;

    public int Seed { get; set; } = 42;
    public double NoiseAmplitude { get; set; } = 0.55;
    public double NoiseFrequency { get; set; } = 3.0;
    public double BoundaryUplift { get; set; } = 0.6;

    public ElevationGenerationStage()
    {
    }

    public ElevationGenerationStage(int seed = 42)
    {
        Seed = seed;
    }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        var vectors = store.GetTileVectors();

        Span<int> neighbors = stackalloc int[6];

        for (int i = 0; i < store.TileCount; i++)
        {
            var plate = plates[i];

            double continentalBase = plate.Elevation * 2.0 - 1.0;
            double noise = SphereNoise.Fbm(vectors[i] * NoiseFrequency, Seed) * NoiseAmplitude;
            double height = continentalBase * 0.45 + noise;

            // Boundary uplift: where a neighboring plate drifts toward this tile,
            // the crust piles up (mountains / island arcs); divergence adds nothing.
            int adjacent = store.GetAdjacent(i, neighbors);
            double uplift = 0.0;
            for (int k = 0; k < adjacent; k++)
            {
                int j = neighbors[k];
                var other = plates[j];
                if (other.Id == plate.Id) continue;

                var dir = (vectors[j] - vectors[i]).Normalize();
                double relX = other.DriftX * other.DriftSpeed - plate.DriftX * plate.DriftSpeed;
                double relY = other.DriftY * other.DriftSpeed - plate.DriftY * plate.DriftSpeed;
                double relZ = other.DriftZ * other.DriftSpeed - plate.DriftZ * plate.DriftSpeed;
                double convergence = -(relX * dir.X + relY * dir.Y + relZ * dir.Z);
                if (convergence > 0) uplift += convergence;
            }

            height += Math.Min(uplift, 2.0) * BoundaryUplift * 0.5;
            elev[i] = new ElevationInfo { Height = (float)height };
        }
    }
}
