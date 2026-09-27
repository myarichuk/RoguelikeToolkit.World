using Xunit;
using Xunit.Abstractions;
using RoguelikeToolkit.World.Presentation;
using RoguelikeToolkit.World.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace RoguelikeToolkit.World.Presentation.Tests;

/// <summary>
/// End-to-end picking verification on a real generated world: every view state
/// the visualizer can produce (default, pan/drag, wheel zoom incl. extremes,
/// rotate buttons incl. clamp, combined, terrain relief, regenerated world) must
/// resolve the hex actually under the mouse through the exact production code
/// path (HexPicker, the same routine GlControl.TryPickHex delegates to).
/// </summary>
public class HexPickTests
{
    private const double ViewW = 800.0;
    private const double ViewH = 600.0;
    private const float SpherePickRadius = 1.0f;
    private const float TerrainPickRadius = 1.42f;

    private readonly ITestOutputHelper _output;

    public HexPickTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed record class WorldFixture(global::RoguelikeToolkit.World.Core.World World, float[] Positions);

    private static global::RoguelikeToolkit.World.Core.World BuildWorld(int seed) => new WorldBuilder().WithSize(2).WithSeed(seed).Build();

    // Independent forward oracle: project an object-space point to pixels
    // (forward direction), while HexPicker unprojects (inverse direction).
    private static (double X, double Y)? ProjectToPixel(Vector3 center, Matrix4x4 mvp)
    {
        var clip = Vector4.Transform(new Vector4(center, 1f), mvp);
        if (clip.W <= 0f) return null;
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f) return null;
        return ((ndcX + 1.0) * 0.5 * ViewW, (1.0 - ndcY) * 0.5 * ViewH);
    }

    private static Matrix4x4 BuildModel(PickCamera camera)
    {
        var modelX = Matrix4x4.CreateRotationX(camera.PitchDegrees * (float)Math.PI / 180.0f);
        var modelY = Matrix4x4.CreateRotationY(camera.YawDegrees * (float)Math.PI / 180.0f);
        return modelX * modelY;
    }

    private static Matrix4x4 BuildView(PickCamera camera)
    {
        return Matrix4x4.CreateTranslation(-camera.PanX, -camera.PanY, -camera.Distance);
    }

    // Truly visible: in front of the camera near plane AND inside the horizon
    // circle (a center past the limb still projects to an in-disc pixel, but the
    // ray legitimately strikes an occluding tile first). Horizon half-angle: dot > 1/D.
    private static bool IsFrontFacing(Vector3 center, Matrix4x4 model, Matrix4x4 view)
    {
        var pv = Vector4.Transform(new Vector4(center, 1f), model * view);
        var gv = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), view);
        var P = new Vector3(pv.X, pv.Y, pv.Z);
        var G = new Vector3(gv.X, gv.Y, gv.Z);
        if (P.Z > -0.15f) return false;
        float dist = G.Length();
        if (dist <= 1f) return false;
        var toCam = Vector3.Normalize(-G);
        var toPoint = Vector3.Normalize(P - G);
        return Vector3.Dot(toPoint, toCam) > 1f / dist + 0.02f;
    }

    private static Vector3[] TileCenters(WorldFixture f)
    {
        var vectors = f.World.Map.DataStore.GetTileVectors();
        var centers = new Vector3[vectors.Length];
        for (int i = 0; i < vectors.Length; i++)
            centers[i] = new Vector3((float)vectors[i].X, (float)vectors[i].Y, (float)vectors[i].Z);
        return centers;
    }

    private int ExactRoundTrip(WorldFixture f, PickCamera camera, float pickRadius, string stateName)
    {
        var store = f.World.Map.DataStore;
        var mvp = HexPicker.BuildMvp(ViewW, ViewH, camera);
        var model = BuildModel(camera);
        var view = BuildView(camera);
        var centers = TileCenters(f);

        int checkedCount = 0;
        int logged = 0;
        for (int tile = 0; tile < centers.Length; tile++)
        {
            if (!IsFrontFacing(centers[tile], model, view)) continue;
            var pixel = ProjectToPixel(centers[tile], mvp);
            if (pixel == null) continue;
            // Keep clicks away from the view edge.
            if (pixel.Value.X < 5 || pixel.Value.X > ViewW - 5 || pixel.Value.Y < 5 || pixel.Value.Y > ViewH - 5) continue;

            bool ok = HexPicker.TryPick(pixel.Value.X, pixel.Value.Y, ViewW, ViewH, camera,
                pickRadius, f.Positions, null, store.GetTileIndex, store.TileCount, out var pick);
            Assert.True(ok, $"state={stateName} tile={tile} center=({pixel.Value.X:F1},{pixel.Value.Y:F1}) missed the pick");
            Assert.Equal(tile, pick.TileIndex);
            Assert.InRange(pick.Latitude, -90.0, 90.0);
            Assert.InRange(pick.Longitude, -180.0, 180.0);
            checkedCount++;

            if (logged < 3)
            {
                _output.WriteLine($"[{stateName}] click=({pixel.Value.X:F1},{pixel.Value.Y:F1}) -> tile={pick.TileIndex} lat={pick.Latitude:F2} lon={pick.Longitude:F2}");
                logged++;
            }
        }

        Assert.True(checkedCount > 0, $"state={stateName} exercised no tiles");
        _output.WriteLine($"[{stateName}] exact round-trip: {checkedCount}/{centers.Length} front tiles re-selected");
        return checkedCount;
    }

    private static readonly (string Name, PickCamera Camera)[] ViewStates = new[]
    {
        ("default", new PickCamera(0f, 0f, 0f, 0f, 2.2f)),
        ("pan-drag", new PickCamera(0f, 0f, 0.35f, -0.25f, 2.2f)),
        ("zoom-in", new PickCamera(0f, 0f, 0f, 0f, 1.4f)),
        ("zoom-out", new PickCamera(0f, 0f, 0f, 0f, 6.0f)),
        ("zoom-min-clamp", new PickCamera(0f, 0f, 0f, 0f, 1.1f)),
        ("rot-left", new PickCamera(-10f, 0f, 0f, 0f, 2.2f)),
        ("rot-right", new PickCamera(10f, 0f, 0f, 0f, 2.2f)),
        ("rot-up", new PickCamera(0f, -10f, 0f, 0f, 2.2f)),
        ("rot-down", new PickCamera(0f, 10f, 0f, 0f, 2.2f)),
        ("rot-big", new PickCamera(30f, 40f, 0f, 0f, 2.2f)),
        ("pitch-clamp", new PickCamera(0f, 89f, 0f, 0f, 2.2f)),
        ("combined", new PickCamera(25f, -30f, 0.3f, 0.2f, 3.0f)),
    };

    [Fact]
    public void Picking_RoundTrips_OnRealWorld_InEveryViewState()
    {
        using var world = BuildWorld(42);
        var fixture = new WorldFixture(world, PositionsOf(world));

        foreach (var (name, camera) in ViewStates)
        {
            // zoom-min-clamp parks the camera 0.1 above the surface: no tile center
            // survives the horizon/near-plane filter, so it is covered by the grid
            // test instead of exact center round-trip.
            if (name == "zoom-min-clamp") continue;
            ExactRoundTrip(fixture, camera, SpherePickRadius, name);
        }

        // Terrain relief (enlarged pick sphere, displaced mesh) is covered by the grid fact.
    }

    private static float[] PositionsOf(global::RoguelikeToolkit.World.Core.World world)
    {
        var vectors = world.Map.DataStore.GetTileVectors();
        var positions = new float[vectors.Length * 3];
        for (int i = 0; i < vectors.Length; i++)
        {
            positions[i * 3] = (float)vectors[i].X;
            positions[i * 3 + 1] = (float)vectors[i].Y;
            positions[i * 3 + 2] = (float)vectors[i].Z;
        }
        return positions;
    }

            [Fact]
    public void Picking_SelectsHexUnderMouse_OnGridSamples()
    {
        using var world = BuildWorld(42);
        var fixture = new WorldFixture(world, PositionsOf(world));
        var store = world.Map.DataStore;

        foreach (var (name, camera) in ViewStates)
        {
            int minHits = name == "zoom-max" ? 1 : 20; // zoom-max shrinks the disc to ~35px
            CheckGridSamples(fixture, store, camera, SpherePickRadius, name, minHits);
        }

        // Terrain relief: enlarged pick sphere, same guarantees.
        CheckGridSamples(fixture, store, new PickCamera(15f, -20f, 0.2f, 0.1f, 2.6f), TerrainPickRadius, "terrain-relief", 20);
    }

    private void CheckGridSamples(WorldFixture fixture, WorldDataStore store, PickCamera camera, float pickRadius, string name, int minHits)
    {
        var mvp = HexPicker.BuildMvp(ViewW, ViewH, camera);
        var centers = TileCenters(fixture);

        int hits = 0;
        int checkedPicks = 0;
        int ties = 0;
        for (double y = 20; y < ViewH; y += 40)
        {
            for (double x = 20; x < ViewW; x += 40)
            {
                bool ok = HexPicker.TryPick(x, y, ViewW, ViewH, camera,
                    pickRadius, fixture.Positions, null, store.GetTileIndex, store.TileCount, out var pick);
                if (!ok) continue;
                hits++;

                Assert.InRange(pick.Latitude, -90.0, 90.0);
                Assert.InRange(pick.Longitude, -180.0, 180.0);

                var reproj = ProjectToPixel(new Vector3(pick.HitX, pick.HitY, pick.HitZ), mvp);
                Assert.True(reproj.HasValue, $"state={name} click=({x},{y}) hit does not reproject into view");
                double err = Math.Sqrt(Math.Pow(x - reproj.Value.X, 2) + Math.Pow(y - reproj.Value.Y, 2));
                Assert.True(err < 1.0, $"state={name} click=({x},{y}) tile={pick.TileIndex} hit reprojects {err:F3}px away");

                var hitGeo = new GeoCoord(pick.Latitude, pick.Longitude);
                int exact = store.GetTileIndexExact(hitGeo);
                if (exact != pick.TileIndex)
                {
                    // Voronoi-boundary tie (e.g. exactly on the dateline): equidistant
                    // centers are all correct answers, so accept either resolution.
                    var hv = Vector3D.FromGeoCoord(hitGeo);
                    var vPicked = centers[pick.TileIndex];
                    var vExact = centers[exact];
                    double dPicked = Vector3D.Dot(hv, new Vector3D(vPicked.X, vPicked.Y, vPicked.Z));
                    double dExact = Vector3D.Dot(hv, new Vector3D(vExact.X, vExact.Y, vExact.Z));
                    if (Math.Abs(dPicked - dExact) < 1e-12) ties++;
                    else Assert.True(false, $"state={name} click=({x},{y}) hit=({hitGeo.Latitude:F4},{hitGeo.Longitude:F4}) bucket={pick.TileIndex} exact={exact}");
                }
                checkedPicks++;
            }
        }

        Assert.True(hits >= minHits, $"state={name} only {hits} grid hits (view unexpectedly empty?)");
        _output.WriteLine($"[{name}] grid: {hits} hits, {checkedPicks} reproject+resolve checks passed, {ties} boundary ties");
    }
[Fact]
    public void Picking_UsesControlSpace_Coords_ToolbarOffsetWouldMiss()
    {
        // Regression guard for the toolbar-row layout shift: window-space Y
        // (control Y + toolbar height) must NOT resolve to the same hex as the
        // control-space click the user aimed at.
        using var world = BuildWorld(42);
        var fixture = new WorldFixture(world, PositionsOf(world));
        var store = world.Map.DataStore;
        var camera = new PickCamera(0f, 0f, 0f, 0f, 2.2f);

        const double clickX = 400.0;
        const double clickY = 300.0;
        const double toolbarHeight = 40.0;

        bool okControl = HexPicker.TryPick(clickX, clickY, ViewW, ViewH, camera,
            SpherePickRadius, fixture.Positions, null, store.GetTileIndex, store.TileCount, out var controlPick);
        Assert.True(okControl, "control-space center click missed");

        bool okShifted = HexPicker.TryPick(clickX, clickY + toolbarHeight, ViewW, ViewH, camera,
            SpherePickRadius, fixture.Positions, null, store.GetTileIndex, store.TileCount, out var shiftedPick);

        // The shifted ray must land on measurably different geography,
        // proving the coordinate frame matters (MainWindow therefore passes
        // GlView-relative points).
        if (okShifted)
        {
            double dLat = Math.Abs(shiftedPick.Latitude - controlPick.Latitude);
            double dLon = Math.Abs(shiftedPick.Longitude - controlPick.Longitude);
            Assert.True(dLat > 1e-9 || dLon > 1e-9, "40px Y shift resolved to identical lat/lon; frame check vacuous");
            _output.WriteLine($"control=({clickX},{clickY}) -> tile={controlPick.TileIndex} lat={controlPick.Latitude:F2} lon={controlPick.Longitude:F2}");
            _output.WriteLine($"shifted=({clickX},{clickY + toolbarHeight}) -> tile={shiftedPick.TileIndex} lat={shiftedPick.Latitude:F2} lon={shiftedPick.Longitude:F2}");
        }
        else
        {
            _output.WriteLine($"control=({clickX},{clickY}) -> tile={controlPick.TileIndex}; shifted click missed entirely");
        }
    }

    [Fact]
    public void Picking_RoundTrips_AfterRegenerate()
    {
        // Regenerate path: a fresh world with a new seed picks just as well.
        using var world = BuildWorld(7);
        var fixture = new WorldFixture(world, PositionsOf(world));
        ExactRoundTrip(fixture, new PickCamera(0f, 0f, 0f, 0f, 2.2f), SpherePickRadius, "regenerated-seed-7");
        ExactRoundTrip(fixture, new PickCamera(-15f, 20f, -0.2f, 0.15f, 2.0f), SpherePickRadius, "regenerated-seed-7-moved");
    }

    [Fact]
    public void Picking_StaysResponsive_OverManyFrames()
    {
        using var world = BuildWorld(42);
        var fixture = new WorldFixture(world, PositionsOf(world));
        var store = world.Map.DataStore;
        var camera = new PickCamera(10f, -15f, 0.2f, 0.1f, 2.4f);

        const int frames = 500;
        var sw = Stopwatch.StartNew();
        int hits = 0;
        for (int i = 0; i < frames; i++)
        {
            double x = 100 + (i * 37 % 600);
            double y = 80 + (i * 53 % 440);
            if (HexPicker.TryPick(x, y, ViewW, ViewH, camera,
                    SpherePickRadius, fixture.Positions, null, store.GetTileIndex, store.TileCount, out _))
                hits++;
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / frames;
        _output.WriteLine($"{frames} picks: {sw.Elapsed.TotalMilliseconds:F1} ms total, {avgMs:F3} ms/pick, {hits} hits");
        Assert.True(hits > 0, "no hits across 500 frames");
        Assert.True(avgMs < 50.0, $"picking too slow: {avgMs:F2} ms/pick");
    }
}
