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
    [InlineData(42, 3, false, "B58259095A40915CD3F166D4E2A5F62D3AC7965347FA5A2A201A477083925AFE")]
    [InlineData(7, 4, false, "DCEC3714CDC1FCFEA955C563D1CE43A904D990C61554EA66003E56E11CEB1DFC")]
    [InlineData(42, 3, true, "10A2C0D257D29B3F226AE7DC25AB275AA8DD70BF71AB3107BD98204CFC86C192")]
    public void LegacyPlates_AreBitStable(int seed, int size, bool tidallyLocked, string expected)
    {
        var actual = Fingerprint(seed, size, tidallyLocked, legacyPlates: true);
        Assert.True(expected == actual, $"legacy golden mismatch seed={seed} size={size} locked={tidallyLocked}: actual {actual}");
    }

    // The default generator (balanced plates). Re-pin deliberately, in its own commit, when generation changes on purpose.
    [Theory]
    [InlineData(42, 3, false, "8DA7A4EA7205376702DD009DF0352D07C732E53B32C9EE4B68E7B7EDCE58B731")]
    [InlineData(7, 4, false, "16FC9C5D4ACEC020B2B6013B25448901F5A1C6CD5859EFE5F556FABE13FA61F1")]
    [InlineData(42, 3, true, "00CB7C2EAF9799795488F5A058333508A0D32026353A2989983B3C535169BD7A")]
    public void Output_IsBitStable(int seed, int size, bool tidallyLocked, string expected)
    {
        var actual = Fingerprint(seed, size, tidallyLocked);
        Assert.True(expected == actual, $"golden mismatch seed={seed} size={size} locked={tidallyLocked}: actual {actual}");
    }
}
