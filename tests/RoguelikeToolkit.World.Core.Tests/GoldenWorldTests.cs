using System.Security.Cryptography;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Sites;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Refactoring safety net: pins a hash of everything observable through the public facade (planet samples, feature
/// catalogs as WKT, a region, a local map, injected sites) for a few (seed, size) pairs. Restructuring Core
/// (file moves, namespace changes, renames, type splits) must never change these values. If one changes, either the
/// refactor altered behaviour (bug) or generation was changed on purpose (then re-pin deliberately, in its own commit).
/// Deliberately avoids naming types that are slated for renaming, so it survives the restructuring unchanged.
/// </summary>
public class GoldenWorldTests
{
    private static string Fingerprint(int seed, int size, bool tidallyLocked)
    {
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed);
        if (tidallyLocked)
            builder.WithPlanet(new PlanetParameters { Insolation = new TidallyLockedInsolation(new Vector3D(1, 0, 0)) });
        using var world = builder.Build();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // Planet-scale samples on a fixed lat/lon lattice.
        for (int lat = -80; lat <= 80; lat += 20)
        for (int lon = -180; lon < 180; lon += 20)
        {
            var c = new GeoCoord(lat, lon);
            w.Write(world.SampleElevation(c));
            var (biome, danger) = world.SampleBiome(c);
            w.Write((int)biome);
            w.Write(danger);
            w.Write(world.Resolve(c).TileIndex);
        }

        // Sparse feature catalogs, via their WKT serialisation.
        for (int i = 0; i < world.Rivers.Rivers.Count; i++) w.Write(world.RiverToWkt(i));
        for (int i = 0; i < world.WaterBodies.Bodies.Count; i++) w.Write(world.WaterBodyToWkt(world.WaterBodies.Bodies[i].Id));
        for (int i = 0; i < world.Deposits.Deposits.Count; i++) w.Write(world.DepositToWkt(i));

        // Region + local tier, with injected sites, on a few tiles spread over the planet.
        var injectors = new ISiteInjector[]
        {
            new SettlementInjector(), new RuinInjector(), new MineInjector(world.Deposits), new LandmarkInjector(),
        };
        foreach (int tile in new[] { 0, world.TileCount / 3, world.TileCount - 1 })
        {
            var region = world.GetRegion(tile);
            foreach (var cell in region.Cells)
            {
                w.Write(cell.Elevation); w.Write((int)cell.Biome); w.Write(cell.Moisture);
                w.Write(cell.IsRiver); w.Write(cell.WaterDepth); w.Write(cell.Flow); w.Write(cell.Seed);
            }
            WriteSites(w, region.WithInjectors(injectors));

            var local = region.GetLocal(region.Cells.Length / 2);
            foreach (var t in local.Tiles)
            {
                w.Write(t.Height); w.Write((int)t.Biome); w.Write(t.Danger); w.Write(t.IsWater);
                w.Write(t.Temperature); w.Write(t.Precipitation); w.Write(t.WaterDepth); w.Write((int)t.ParentBiome);
            }
            WriteSites(w, local.WithInjectors(injectors));
        }

        w.Flush();
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    private static void WriteSites(BinaryWriter w, SiteCatalog catalog)
    {
        foreach (var s in catalog.Sites)
        {
            w.Write((int)s.Kind); w.Write(s.CellIndex); w.Write(s.NameSeed); w.Write(s.InjectorId);
        }
    }

    [Theory]
    [InlineData(42, 3, false, "2EB65AA66A4968BC3964CCFB04904F6728C5C2CC661AE9056C4AF66A8A26DAB6")]
    [InlineData(7, 4, false, "CA4E6556AE0A1B974B1CB4A92B219D4D78B68A53888AE2388FC61344343C5E44")]
    [InlineData(42, 3, true, "EDE258007614DD16798E182E5DFBC548E3CCC8B310554BC7E3D845DD8AB91584")]
    public void Output_IsBitStable(int seed, int size, bool tidallyLocked, string expected)
    {
        var actual = Fingerprint(seed, size, tidallyLocked);
        Assert.True(expected == actual, $"golden mismatch seed={seed} size={size} locked={tidallyLocked}: actual {actual}");
    }
}
