using System.IO;
using System.Runtime.CompilerServices;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class PlateTableTests
{
    [Fact]
    public void TectonicPlate_IsCompact()
    {
        // Was 72 bytes per tile before plate-wide data moved to a table.
        Assert.Equal(16, Unsafe.SizeOf<TectonicPlate>());
        Assert.Equal(40, Unsafe.SizeOf<PlateInfo>());
    }

    [Fact]
    public void Table_IsSeparateFromTileLayers()
    {
        using var store = new WorldDataStore(1);
        store.RegisterLayer<TectonicPlate>();
        store.RegisterTable<PlateInfo>(7);
        store.Allocate();

        Assert.Equal(store.TileCount, store.GetSpan<TectonicPlate>().Length);
        Assert.Equal(7, store.GetTable<PlateInfo>().Length);
        Assert.True(store.IsTableRegistered<PlateInfo>());
        Assert.False(store.IsTableRegistered<TectonicPlate>());
        Assert.Throws<InvalidOperationException>(() => store.GetSpan<PlateInfo>());

        store.GetTable<PlateInfo>()[6].DriftSpeed = 3.5;
        store.ClearLayer(typeof(PlateInfo));
        Assert.Equal(0.0, store.GetTable<PlateInfo>()[6].DriftSpeed);
    }

    [Fact]
    public void Table_RegisteredTwiceWithDifferentLength_Throws()
    {
        using var store = new WorldDataStore(1);
        store.RegisterTable<PlateInfo>(7);
        store.RegisterTable<PlateInfo>(7); // idempotent
        Assert.Throws<InvalidOperationException>(() => store.RegisterTable<PlateInfo>(8));
        Assert.Throws<InvalidOperationException>(() => store.RegisterLayer<PlateInfo>());
    }

    [Fact]
    public void Stage_WithMorePlatesThanTable_FailsClearly()
    {
        using var map = new WorldMap(1);
        using var layer = new TectonicPlateLayer(map.DataStore, 4);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();
        var stage = new TectonicPlateGenerationStage(TectonicPlateLayer.MinPlateCapacity + 1, 1);
        Assert.Throws<InvalidOperationException>(() => stage.Execute(map));
    }

    [Fact]
    public void TileIds_IndexTheTable()
    {
        using var map = new WorldMap(2);
        using var layer = new TectonicPlateLayer(map.DataStore, 9, 5);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();
        new TectonicPlateGenerationStage(9, 5).Execute(map);

        var tiles = map.DataStore.GetSpan<TectonicPlate>();
        var table = map.DataStore.GetTable<PlateInfo>();
        foreach (var t in tiles)
        {
            Assert.InRange((int)t.Id, 1, 9);
            var p = table[t.Id - 1];
            Assert.InRange(p.DriftX * p.DriftX + p.DriftY * p.DriftY + p.DriftZ * p.DriftZ, 0.999, 1.001);
        }
        Assert.Equal(float.MaxValue, new TectonicPlate { BoundaryRings = byte.MaxValue }.BoundaryDistance);
        Assert.Equal(3f, new TectonicPlate { BoundaryRings = 3 }.BoundaryDistance);
    }

    [Fact]
    public void FileBacked_RoundTripsAndRejectsDifferentTableLength()
    {
        string name = "plates-" + Guid.NewGuid().ToString("N") + ".wds";
        string path = Path.Combine(Environment.CurrentDirectory, name);
        try
        {
            using (var a = new WorldDataStore(1, name))
            {
                a.RegisterLayer<TectonicPlate>();
                a.RegisterTable<PlateInfo>(5);
                a.Allocate();
                a.GetTable<PlateInfo>()[4].Elevation = 0.25;
                a.GetSpan<TectonicPlate>()[3].Id = 4;
            }
            using (var b = new WorldDataStore(1, name))
            {
                b.RegisterLayer<TectonicPlate>();
                b.RegisterTable<PlateInfo>(5);
                b.Allocate();
                Assert.Equal(0.25, b.GetTable<PlateInfo>()[4].Elevation);
                Assert.Equal(4, b.GetSpan<TectonicPlate>()[3].Id);
            }
            using var c = new WorldDataStore(1, name);
            c.RegisterLayer<TectonicPlate>();
            c.RegisterTable<PlateInfo>(6);
            Assert.Throws<InvalidDataException>(() => c.Allocate());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
