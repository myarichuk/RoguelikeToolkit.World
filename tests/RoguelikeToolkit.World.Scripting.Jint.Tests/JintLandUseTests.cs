using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Scripting.Jint;
using Xunit;

namespace RoguelikeToolkit.World.Scripting.Jint.Tests;

/// <summary>Land use through the sandbox: untrusted scripts get the same capacity and decision layers as compiled plugins.</summary>
public class JintLandUseTests
{
    private static string Sample() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "samples", "JintScripts", "herders-and-farmers.js"));

    private static JintWorldStage Stage(string source, Type[]? reads = null, Type[]? writes = null, int order = 30)
        => new(new JintStageSpec
        {
            Name = "land-js",
            Order = order,
            Source = source,
            Reads = reads ?? new[] { typeof(LandCapacityInfo), typeof(LandUseInfo) },
            Writes = writes ?? new[] { typeof(LandUseInfo) },
        });

    private static Core.World Build(JintWorldStage stage) => new WorldBuilder().WithSize(3).WithSeed(42).WithLandUse().AddStage(stage).Build();

    [Fact]
    public void TheSampleScript_AllocatesFromCapacity_Deterministically()
    {
        using var a = Build(Stage(Sample()));
        using var b = Build(Stage(Sample()));
        var use = a.Map.DataStore.GetSpan<LandUseInfo>().ToArray();
        var cap = a.Map.DataStore.GetSpan<LandCapacityInfo>().ToArray();
        Assert.Equal(use, b.Map.DataStore.GetSpan<LandUseInfo>().ToArray());
        Assert.Contains(use, u => u.Use == LandUseId.Farmland);
        Assert.Contains(use, u => u.Use == LandUseId.Pasture);
        for (int i = 0; i < use.Length; i++)
        {
            Assert.Equal(cap[i].Arable >= 128, use[i].Use == LandUseId.Farmland);
            Assert.Equal(cap[i].Game >= 128, (use[i].Activities & (byte)LandActivity.Hunting) != 0);
            Assert.Equal(cap[i].Fish >= 64, (use[i].Activities & (byte)LandActivity.Fishing) != 0);
        }
    }

    [Fact]
    public void AHandPin_WinsOverTheScript()
    {
        using var w = Build(Stage(Sample()));
        var use = w.Map.DataStore.GetSpan<LandUseInfo>().ToArray();
        int farm = Array.FindIndex(use, u => u.Use == LandUseId.Farmland);
        w.Edits[farm].Flags |= UserEditFlags.PinLandUse;     // pin "no land use"
        w.Recompute();
        Assert.Equal(default, w.Map.DataStore.GetSpan<LandUseInfo>()[farm]);
    }

    [Fact]
    public void WritingWithoutDeclaringIt_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Build(Stage("function execute() { setLandUse(0, 1); }", reads: new[] { typeof(LandCapacityInfo) }, writes: Array.Empty<Type>())));
        Assert.Contains("without declaring it in Writes", ex.ToString());
    }

    [Theory]
    [InlineData("setLandUse(0, 256)")]
    [InlineData("setActivities(0, -1)")]
    [InlineData("setIntensity(0, 1000)")]
    public void OutOfRangeValues_Throw(string call)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(Stage($"function execute() {{ {call}; }}")));
        Assert.Contains("out of range", ex.ToString());
    }

    [Fact]
    public void Constants_MatchTheCoreIds()
    {
        using var w = new WorldBuilder().WithSize(0).WithSeed(1).WithLandUse().AddStage(Stage(
            "function execute() { setLandUse(0, LANDUSE.Built); setActivities(0, ACTIVITY.Hunting | ACTIVITY.Fishing); setIntensity(0, 7); }")).Build();
        var u = w.Map.DataStore.GetSpan<LandUseInfo>()[0];
        Assert.Equal(LandUseId.Built, u.Use);
        Assert.Equal((byte)(LandActivity.Hunting | LandActivity.Fishing), u.Activities);
        Assert.Equal(7, u.Intensity);
    }
}
