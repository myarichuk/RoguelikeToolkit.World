using System;
using System.Collections.Generic;
using System.IO;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Scripting.Jint;
using Xunit;

namespace RoguelikeToolkit.World.Scripting.Jint.Tests;

public class JintWorldStageTests
{
    private static WorldMap TinyMap()
    {
        var map = new WorldMap(0);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, 42, new TectonicPlateLayer(map.DataStore, 4, 42)));
        map.DataStore.Allocate();
        return map;
    }

    private static JintWorldStage AridityStage(string? source = null, int order = 19)
        => new(new JintStageSpec
        {
            Name = "aridity-js",
            Order = order,
            Source = source ?? File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                    "samples", "JintScripts", "aridity.js")),
            Reads = new[] { typeof(ClimateInfo) },
            Writes = new[] { typeof(ClimateInfo) },
            Params = new Dictionary<string, object?> { ["dryness"] = 0.5 },
        });

    [Fact]
    public void HalvesPrecipitation_Deterministically()
    {
        using var a = TinyMap();
        using var b = TinyMap();
        foreach (var m in new[] { a, b })
        {
            var c = m.DataStore.GetSpan<ClimateInfo>();
            for (int i = 0; i < c.Length; i++)
                c[i] = new ClimateInfo { Temperature = 0.7f, Precipitation = 0.8f };
        }

        AridityStage().Execute(a);
        AridityStage().Execute(b);

        var ca = a.DataStore.GetSpan<ClimateInfo>();
        var cb = b.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < ca.Length; i++)
        {
            Assert.Equal(cb[i].Precipitation, ca[i].Precipitation);
            Assert.True(ca[i].Precipitation < 0.8f, $"tile {i} not dried");
            Assert.True(ca[i].Precipitation >= 0.0f);
        }
    }

    [Fact]
    public void MissingExecute_ThrowsWithStageName()
    {
        using var map = TinyMap();
        var stage = new JintWorldStage(new JintStageSpec
        {
            Name = "broken-js",
            Order = 19,
            Source = "var x = 1;",
            Reads = new[] { typeof(ClimateInfo) },
            Writes = new[] { typeof(ClimateInfo) },
        });

        var ex = Assert.Throws<InvalidOperationException>(() => stage.Execute(map));
        Assert.Contains("broken-js", ex.Message);
        Assert.Contains("execute()", ex.Message);
    }

    [Fact]
    public void UndeclaredWrite_Throws()
    {
        using var map = TinyMap();
        var stage = new JintWorldStage(new JintStageSpec
        {
            Name = "sneaky-js",
            Order = 19,
            Source = "function execute() { setHeight(0, 9.9); }",
            Reads = new[] { typeof(ClimateInfo) },
            Writes = new[] { typeof(ClimateInfo) },
        });

        var ex = Assert.Throws<InvalidOperationException>(() => stage.Execute(map));
        Assert.Contains("sneaky-js", ex.Message);
        Assert.Contains("ElevationInfo", ex.Message);
    }

    [Fact]
    public void InfiniteLoop_HitsBudget()
    {
        using var map = TinyMap();
        var stage = new JintWorldStage(new JintStageSpec
        {
            Name = "loopy-js",
            Order = 19,
            Source = "function execute() { while (true) {} }",
            Reads = new[] { typeof(ClimateInfo) },
            Writes = new[] { typeof(ClimateInfo) },
            Limits = new JintStageLimits
            {
                Timeout = TimeSpan.FromSeconds(2),
                MaxStatements = 10_000,
                MemoryLimitBytes = 16_000_000,
            },
        });

        Assert.Throws<InvalidOperationException>(() => stage.Execute(map));
    }

    [Fact]
    public void NoClrAccess()
    {
        using var map = TinyMap();
        var stage = new JintWorldStage(new JintStageSpec
        {
            Name = "evil-js",
            Order = 19,
            Source = "function execute() { System.IO.File.ReadAllText('/etc/passwd'); }",
            Reads = new[] { typeof(ClimateInfo) },
            Writes = new[] { typeof(ClimateInfo) },
        });

        Assert.Throws<InvalidOperationException>(() => stage.Execute(map));
    }

    [Fact]
    public void BlindSecondWriter_ViaPipeline_Throws()
    {
        using var map = TinyMap();
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new JintWorldStage(new JintStageSpec
        {
            Name = "first-js",
            Order = 19,
            Source = "function execute() { setPrecip(0, 0.1); }",
            Writes = new[] { typeof(ClimateInfo) },
        }));
        pipeline.AddStage(new JintWorldStage(new JintStageSpec
        {
            Name = "second-js",
            Order = 21,
            Source = "function execute() { setPrecip(0, 0.2); }",
            Writes = new[] { typeof(ClimateInfo) },
        }));

        Assert.Throws<InvalidOperationException>(() => pipeline.Execute(map));
    }

    [Fact]
    public void SampleScripts_ParseAndRun()
    {
        string samples = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "samples", "JintScripts");
        foreach (string file in Directory.GetFiles(samples, "*.js"))
        {
            using var map = TinyMap();
            var climate = map.DataStore.GetSpan<ClimateInfo>();
            for (int i = 0; i < climate.Length; i++)
                climate[i] = new ClimateInfo { Temperature = 0.7f, Precipitation = 0.8f };
            var locals = map.DataStore.GetSpan<LocalMapInfo>();
            for (int i = 0; i < locals.Length; i++)
                locals[i] = new LocalMapInfo { Biome = BiomeType.Plains, DangerLevel = 1 };

            var stage = new JintWorldStage(new JintStageSpec
            {
                Name = Path.GetFileName(file),
                Order = 21,
                Source = File.ReadAllText(file),
                Reads = new[] { typeof(ClimateInfo), typeof(LocalMapInfo) },
                Writes = new[] { typeof(ClimateInfo), typeof(LocalMapInfo) },
            });

            stage.Execute(map); // must not throw
        }
    }
}
