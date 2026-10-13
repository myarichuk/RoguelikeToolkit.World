using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Shear-aware boundary classification with a soft gate, and a polarity for oceanic-oceanic subduction. Both are opt-in on the stage and on in the balanced preset.</summary>
public class TectonicBoundaryTests
{
    private static TectonicPlate[] Plates(int seed, bool shear, bool polarity, int size = 5, int plates = 16)
    {
        using var map = new WorldMap(size);
        using var layer = new TectonicPlateLayer(map.DataStore, plates);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new TectonicPlateGenerationStage(plates, seed) { ShearTransform = shear, OceanicPolarity = polarity });
        pipeline.Execute(map);
        return map.DataStore.GetSpan<TectonicPlate>().ToArray();
    }

    [Fact]
    public void ConvergentDriver_GivesTheDivingOceanicSideATrenchAndLeavesTheOtherAnArc()
    {
        double arc = TectonicPlateGenerationStage.ConvergentDriver(CrustType.Oceanic, CrustType.Oceanic, 0.5);
        double trench = TectonicPlateGenerationStage.ConvergentDriver(CrustType.Oceanic, CrustType.Oceanic, 0.5, subducting: true);
        Assert.True(arc > 0, "the overriding side keeps the island arc");
        Assert.True(trench < 0, "the diving side takes the trench");
        // Nothing else reads the flag: a continental plate over an oceanic one is an arc whatever it says.
        Assert.Equal(
            TectonicPlateGenerationStage.ConvergentDriver(CrustType.Continental, CrustType.Oceanic, 0.5),
            TectonicPlateGenerationStage.ConvergentDriver(CrustType.Continental, CrustType.Oceanic, 0.5, subducting: false));
    }

    [Fact]
    public void OceanicPolarity_TurnsOneSideOfOceanicConvergenceIntoTrench_AndOffKeepsTheLegacyRelief()
    {
        // Oceanic tiles at a convergent border: legacy raises all of them (an arc on both sides); with polarity about half dive.
        static (int Trench, int Arc) Count(TectonicPlate[] p)
        {
            int trench = 0, arc = 0;
            foreach (var t in p)
            {
                if (t.Boundary != PlateBoundaryType.Convergent || t.Crust != CrustType.Oceanic || t.BoundaryRings != 0) continue;
                if (t.Orogeny < 0) trench++; else arc++;
            }
            return (trench, arc);
        }
        int trenchOff = 0, arcOff = 0, trenchOn = 0, arcOn = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var (to, ao) = Count(Plates(seed, shear: false, polarity: false));
            var (tn, an) = Count(Plates(seed, shear: false, polarity: true));
            trenchOff += to; arcOff += ao; trenchOn += tn; arcOn += an;
        }
        // Legacy already trenches an oceanic plate that meets a continental one, so those tiles are in both counts; the
        // oceanic-oceanic pairs are what polarity adds (about 1.7x over these seeds).
        Assert.True(trenchOn > trenchOff * 1.25, $"trench tiles {trenchOff} -> {trenchOn}");
        Assert.True(arcOn < arcOff, $"arc tiles {arcOff} -> {arcOn}");
    }

    [Fact]
    public void ShearTransform_FadesTheDriverInInsteadOfSwitchingItOn()
    {
        // Legacy: every divergent border tile carries exactly +0.5 (ridge) or -0.55 (rift) however feeble the spreading.
        // Soft gate: the driver blends from the transform value, so intermediate drivers exist.
        static (int Total, int Graded) Divergent(TectonicPlate[] p)
        {
            int total = 0, graded = 0;
            foreach (var t in p)
            {
                if (t.Boundary != PlateBoundaryType.Divergent || t.BoundaryRings != 0) continue;
                total++;
                if (Math.Abs(t.Orogeny - 0.5f) > 1e-4f && Math.Abs(t.Orogeny + 0.55f) > 1e-4f) graded++;
            }
            return (total, graded);
        }
        int gradedOff = 0, gradedOn = 0, totalOn = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            gradedOff += Divergent(Plates(seed, false, false)).Graded;
            var (t, g) = Divergent(Plates(seed, true, false));
            totalOn += t; gradedOn += g;
        }
        Assert.Equal(0, gradedOff);
        Assert.True(totalOn > 50, "non-vacuity: there must be divergent borders to grade");
        Assert.True(gradedOn > totalOn / 10, $"only {gradedOn} of {totalOn} divergent border tiles are graded");
    }

    [Fact]
    public void ShearTransform_CallsObliqueBordersTransform()
    {
        // A border whose plates mostly slide along it is a fault, not a collision.
        static int Transforms(TectonicPlate[] p) => p.Count(t => t.BoundaryRings == 0 && t.Boundary == PlateBoundaryType.Transform);
        int off = 0, on = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            off += Transforms(Plates(seed, false, false));
            on += Transforms(Plates(seed, true, false));
        }
        Assert.True(on > off * 2, $"transform border tiles {off} -> {on}");
    }

    [Fact]
    public void BothKnobs_AreOffOnTheStageAndOnInTheBalancedPreset()
    {
        var stage = new TectonicPlateGenerationStage(8, 1);
        Assert.False(stage.ShearTransform);
        Assert.False(stage.OceanicPolarity);
        TectonicPlateGenerationStage? configured = null;
        using (new WorldBuilder().WithSize(2).WithSeed(1).WithTectonics(t => configured = t).Build()) { }
        Assert.True(configured!.ShearTransform);
        Assert.True(configured.OceanicPolarity);
        using (new WorldBuilder().WithSize(2).WithSeed(1).WithLegacyPlates().WithTectonics(t => configured = t).Build()) { }
        Assert.False(configured!.ShearTransform);
        Assert.False(configured.OceanicPolarity);
    }
}
