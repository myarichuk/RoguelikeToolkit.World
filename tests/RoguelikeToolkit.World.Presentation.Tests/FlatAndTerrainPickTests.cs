using System;
using System.Numerics;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Presentation.Tests;

/// <summary>
/// Picks must return the tile actually drawn under the mouse on flat maps (drawn on z = 0) and on displaced terrain.
/// The sphere-only picker used to miss everything beyond ~55 degrees of longitude on a flat map and pulled the rest
/// towards the centre; on terrain it intersected a fixed r = 1.42 sphere well above the ground.
/// </summary>
public class FlatAndTerrainPickTests(ITestOutputHelper output)
{
    private const double ViewW = 800.0, ViewH = 600.0;

    private static (double X, double Y)? ToPixel(Vector3 p, Matrix4x4 mvp)
    {
        var clip = Vector4.Transform(new Vector4(p, 1f), mvp);
        if (clip.W <= 0f) return null;
        float x = clip.X / clip.W, y = clip.Y / clip.W;
        if (x < -0.98f || x > 0.98f || y < -0.98f || y > 0.98f) return null;
        return ((x + 1.0) * 0.5 * ViewW, (1.0 - y) * 0.5 * ViewH);
    }

    public static TheoryData<string> Projections() => new() { "equirect", "mercator", "gnomonic" };

    private static IProjection Make(string name) => name switch
    {
        "equirect" => new EquirectangularProjection(1.0),
        "mercator" => new MercatorProjection(1.0),
        _ => new GnomonicProjection(1.0),
    };

    [Theory]
    [MemberData(nameof(Projections))]
    public void FlatMap_PicksTheTileUnderTheMouse(string name)
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var projection = Make(name);
        var camera = new PickCamera(0f, 0f, 0f, 0f, 4.5f);
        var mvp = HexPicker.BuildMvp(ViewW, ViewH, camera);
        var positions = new float[] { 0f, 0f, 0f };

        int checkedTiles = 0, far = 0;
        for (int tile = 0; tile < store.TileCount; tile++)
        {
            var geo = store.GetGeoCoord(tile);
            var p = projection.Project(geo);
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 50 || Math.Abs(p.Y) > 50) continue;
            var pixel = ToPixel(new Vector3((float)p.X, (float)p.Y, 0f), mvp);
            if (pixel == null) continue;

            Assert.True(HexPicker.TryPick(pixel.Value.X, pixel.Value.Y, ViewW, ViewH, camera, 1f, positions, projection,
                store.GetTileIndex, store.TileCount, out var pick), $"{name}: tile {tile} at {geo.Latitude:F1},{geo.Longitude:F1} missed");
            Assert.Equal(tile, pick.TileIndex);
            Assert.Equal(0f, pick.HitZ, 4);
            checkedTiles++;
            if (Math.Abs(p.X) > 1.2) far++;
        }
        output.WriteLine($"{name}: {checkedTiles} tiles round-tripped, {far} beyond |x| 1.2");
        Assert.True(checkedTiles > 50, $"{name}: only {checkedTiles} tiles in view");
        if (name != "gnomonic") Assert.True(far > 0, "no tile beyond the old unit-sphere reach was exercised");
    }

    [Fact]
    public void FlatMap_ClickOffTheMap_Misses()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(1).Build();
        var store = world.Map.DataStore;
        // Zoomed out far enough that the equirectangular map (x in +-pi) leaves empty margins left and right.
        var camera = new PickCamera(0f, 0f, 0f, 0f, 12f);
        Assert.False(HexPicker.TryPick(5, ViewH / 2, ViewW, ViewH, camera, 1f, new float[] { 0f, 0f, 0f },
            new EquirectangularProjection(1.0), store.GetTileIndex, store.TileCount, out _));
        Assert.True(HexPicker.TryPick(ViewW / 2, ViewH / 2, ViewW, ViewH, camera, 1f, new float[] { 0f, 0f, 0f },
            new EquirectangularProjection(1.0), store.GetTileIndex, store.TileCount, out _));
    }

    [Fact]
    public void Terrain_PicksTheDisplacedTileUnderTheMouse()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var vectors = store.GetTileVectors();
        // Mild, uneven relief: every tile its own radius in [1, 1.08].
        float Radius(int t) => 1f + 0.08f * ((unchecked((uint)t * 2654435761u) >> 24) / 255f);
        var camera = new PickCamera(0f, 0f, 0f, 0f, 2.6f);
        var mvp = HexPicker.BuildMvp(ViewW, ViewH, camera);
        var positions = new float[] { 1f, 0f, 0f };
        var toCamera = Vector3.Normalize(new Vector3(0f, 0f, 1f));

        int checkedTiles = 0, exact = 0, oldExact = 0;
        for (int tile = 0; tile < store.TileCount; tile++)
        {
            var dir = new Vector3((float)vectors[tile].X, (float)vectors[tile].Y, (float)vectors[tile].Z);
            if (Vector3.Dot(dir, toCamera) < 0.85f) continue; // within ~30 degrees of the view centre: no occlusion by relief
            var pixel = ToPixel(dir * Radius(tile), mvp);
            if (pixel == null) continue;
            checkedTiles++;

            if (HexPicker.TryPick(pixel.Value.X, pixel.Value.Y, ViewW, ViewH, camera, 1.42f, positions, null,
                    store.GetTileIndex, store.TileCount, Radius, out var pick) && pick.TileIndex == tile)
                exact++;
            if (HexPicker.TryPick(pixel.Value.X, pixel.Value.Y, ViewW, ViewH, camera, 1.42f, positions, null,
                    store.GetTileIndex, store.TileCount, out var old) && old.TileIndex == tile)
                oldExact++;
        }
        output.WriteLine($"terrain: {exact}/{checkedTiles} exact (fixed 1.42 sphere: {oldExact})");
        Assert.True(checkedTiles > 20);
        Assert.Equal(checkedTiles, exact);
    }
}
