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
    private static string Fingerprint(int seed, int size, bool tidallyLocked, bool legacyPlates = false)
    {
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed);
        if (legacyPlates) builder.WithLegacyPlates();
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

    // The pre-balanced generator, pinned with the hashes it had before balanced plates became the default:
    // WithLegacyPlates() must keep reproducing those worlds bit for bit.
    [Theory]
    [InlineData(42, 3, false, "D43E534E49575D9BF25B66198CB9DF13DB452122EEDF56D063C2DD1A0831F107")]
    [InlineData(7, 4, false, "E4EEED798432B8E73CBA58C1C856ED1E53AAD8651D71B43DA8521548488D3EAB")]
    [InlineData(42, 3, true, "8209A4830BC2617CEECDD8058C8373E5BC5295A2D807100995E0ED275D7324B7")]
    public void LegacyPlates_AreBitStable(int seed, int size, bool tidallyLocked, string expected)
    {
        var actual = Fingerprint(seed, size, tidallyLocked, legacyPlates: true);
        Assert.True(expected == actual, $"legacy golden mismatch seed={seed} size={size} locked={tidallyLocked}: actual {actual}");
    }

    // The default generator (balanced plates). Re-pin deliberately, in its own commit, when generation changes on purpose.
    [Theory]
    [InlineData(42, 3, false, "B923330F2F662ACDA07FE524CFE9F42865E777632455F25D0FC0CE02573AF1DB")]
    [InlineData(7, 4, false, "0C3DBE467A8E945A602BDC3FA5A0C88BA681D080D8FCD8BD158FAA56347C7460")]
    [InlineData(42, 3, true, "DB883968BF8BA90AB70AD7E4E9A4B69BB8C7DB8EF912ADF8791273570ED2760C")]
    public void Output_IsBitStable(int seed, int size, bool tidallyLocked, string expected)
    {
        var actual = Fingerprint(seed, size, tidallyLocked);
        Assert.True(expected == actual, $"golden mismatch seed={seed} size={size} locked={tidallyLocked}: actual {actual}");
    }
}
