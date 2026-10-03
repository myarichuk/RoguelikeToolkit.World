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
    public void StatementBudget_ScalesWithTileCount_UnlessPinned()
    {
        var scaled = new JintStageLimits { BaseStatements = 1_000, StatementsPerTile = 50 };
        Assert.Equal(1_000 + 50 * 163_842, scaled.EffectiveMaxStatements(163_842));
        Assert.Equal(int.MaxValue, new JintStageLimits { BaseStatements = int.MaxValue, StatementsPerTile = 50 }.EffectiveMaxStatements(1_000_000));
        Assert.Equal(7, new JintStageLimits { MaxStatements = 7 }.EffectiveMaxStatements(163_842));
        Assert.Equal(16_000_000 + 2_048L * 163_842, new JintStageLimits().EffectiveMemoryLimitBytes(163_842));
        Assert.Equal(1_000_000L, new JintStageLimits { MemoryLimitBytes = 1_000_000 }.EffectiveMemoryLimitBytes(163_842));
        // The default must admit a per-tile pass over a size-7 world.
        Assert.True(new JintStageLimits().EffectiveMaxStatements(163_842) >= 163_842 * 50);
    }

    [Fact]
    public void PerTileLoop_FitsScaledBudget_ButNotAnEqualFixedCap()
    {
        const string source = "function execute() { for (var i = 0; i < TILE_COUNT; i++) { setTemp(i, temp(i)); } }";
        JintWorldStage Make(JintStageLimits limits) => new(new JintStageSpec
        {
            Name = "loop-js", Order = 19, Source = source,
            Reads = new[] { typeof(ClimateInfo) }, Writes = new[] { typeof(ClimateInfo) },
            Limits = limits,
        });

        using var map = TinyMap();
        int tiles = map.DataStore.TileCount;

        // A pinned cap below one statement per tile trips; the per-tile term is what admits the loop.
        Make(new JintStageLimits { BaseStatements = 100, StatementsPerTile = 20 }).Execute(map);
        Assert.Throws<InvalidOperationException>(() =>
            Make(new JintStageLimits { MaxStatements = tiles / 2 }).Execute(map));
    }

    [Fact]
    public void SizeSevenSweep_FitsDefaultBudgets()
    {
        // 163,842 tiles: a fixed 250k-statement / 16 MB cap (the old defaults) cannot
        // survive one read+write per tile; the scaled defaults must, with no limits set.
        using var map = new WorldMap(7);
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.DataStore.Allocate();
        Assert.True(map.DataStore.TileCount > 100_000);
        var stage = new JintWorldStage(new JintStageSpec
        {
            Name = "sweep-js", Order = 19,
            Source = "function execute() { for (var i = 0; i < TILE_COUNT; i++) { setTemp(i, temp(i) * 0.99); } }",
            Reads = new[] { typeof(ClimateInfo) }, Writes = new[] { typeof(ClimateInfo) },
            Limits = new JintStageLimits { Timeout = TimeSpan.FromSeconds(30) },
        });
        stage.Execute(map);
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
