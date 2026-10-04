using RoguelikeToolkit.World.Core.Entities;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class EntityStoreTests
{
    private struct Pos { public int Tile; public ushort X, Y; }
    private struct Info { public ushort Species; public byte Age; public uint NameSeed; }

    private static EntityStore NewStore(int capacity = 4)
    {
        var store = new EntityStore(capacity);
        store.AddColumn<Pos>();
        store.AddColumn<Info>();
        return store;
    }

    [Fact]
    public void Create_Destroy_StaleHandlesAreRejected()
    {
        var store = NewStore();
        var a = store.Create();
        Assert.True(store.IsAlive(a));
        Assert.True(store.Destroy(a));
        Assert.False(store.IsAlive(a));
        Assert.False(store.Destroy(a));
        Assert.Throws<ArgumentException>(() => store.GetColumn<Pos>()[a]);

        var b = store.Create();                 // recycles the slot...
        Assert.Equal(a.Index, b.Index);
        Assert.NotEqual(a.Generation, b.Generation);
        Assert.False(store.IsAlive(a));         // ...but the old handle stays dead
        Assert.True(store.IsAlive(b));
        Assert.False(store.IsAlive(EntityId.None));
    }

    [Fact]
    public void NewEntity_StartsZeroed_EvenOnRecycledSlot()
    {
        var store = NewStore();
        var pos = store.GetColumn<Pos>();
        var a = store.Create();
        pos[a] = new Pos { Tile = 99, X = 3, Y = 4 };
        store.Destroy(a);
        var b = store.Create();
        Assert.Equal(0, pos[b].Tile);
    }

    [Fact]
    public void Growth_KeepsColumnData_AndCountsAreRight()
    {
        var store = NewStore(2);
        var info = store.GetColumn<Info>();
        var ids = new List<EntityId>();
        for (int i = 0; i < 5000; i++)
        {
            var id = store.Create();
            info[id] = new Info { Species = (ushort)i, Age = (byte)(i & 0x7F), NameSeed = (uint)(i * 7) };
            ids.Add(id);
        }
        Assert.Equal(5000, store.Count);
        for (int i = 0; i < 5000; i++) Assert.Equal((uint)(i * 7), info[ids[i]].NameSeed);
        for (int i = 0; i < 5000; i += 2) store.Destroy(ids[i]);
        Assert.Equal(2500, store.Count);
        Assert.Equal(5000, store.HighWater);
    }

    [Fact]
    public void SlotReuse_IsDeterministic()
    {
        static List<int> Run()
        {
            var store = NewStore();
            var ids = Enumerable.Range(0, 20).Select(_ => store.Create()).ToList();
            foreach (var i in new[] { 3, 9, 4, 15 }) store.Destroy(ids[i]);
            return Enumerable.Range(0, 6).Select(_ => store.Create().Index).ToList();
        }
        Assert.Equal(Run(), Run());
        Assert.Equal(new[] { 15, 4, 9, 3, 20, 21 }, Run());
    }

    [Fact]
    public void Snapshot_RoundTrips()
    {
        var src = NewStore();
        var pos = src.GetColumn<Pos>();
        var ids = Enumerable.Range(0, 3000).Select(_ => src.Create()).ToList();
        for (int i = 0; i < ids.Count; i++) pos[ids[i]] = new Pos { Tile = i, X = (ushort)i };
        src.Destroy(ids[10]); src.Destroy(ids[2500]);

        using var ms = new MemoryStream();
        src.Save(ms);
        ms.Position = 0;

        var dst = NewStore(1);
        dst.Load(ms);
        Assert.Equal(src.Count, dst.Count);
        Assert.Equal(src.HighWater, dst.HighWater);
        Assert.False(dst.IsAlive(ids[10]));
        Assert.True(dst.IsAlive(ids[11]));
        Assert.Equal(11, dst.GetColumn<Pos>()[ids[11]].Tile);

        // Free list survives: next creates reuse the destroyed slots in the same order.
        Assert.Equal(src.Create().Index, dst.Create().Index);
        Assert.Equal(src.Create().Index, dst.Create().Index);
        Assert.Equal(src.Create().Index, dst.Create().Index);   // grows past the loaded capacity
    }

    [Fact]
    public void Load_RejectsMismatchedColumns_AndLeavesStoreIntact()
    {
        var src = NewStore();
        src.Create();
        using var ms = new MemoryStream();
        src.Save(ms);

        var other = new EntityStore();
        other.AddColumn<Pos>();         // missing the Info column
        var keep = other.Create();
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => other.Load(ms));
        Assert.True(other.IsAlive(keep));

        ms.Position = 0;
        var wrong = new EntityStore();
        wrong.AddColumn<Pos>();
        wrong.AddColumn<long>();
        Assert.Throws<InvalidDataException>(() => wrong.Load(ms));
    }

    [Fact]
    public void AddColumn_AfterEntitiesExist_Throws()
    {
        var store = new EntityStore();
        store.AddColumn<Pos>();
        store.Create();
        Assert.Throws<InvalidOperationException>(() => store.AddColumn<Info>());
        Assert.Same(store.GetColumn<Pos>(), store.AddColumn<Pos>());
    }

    [Fact]
    public void Spans_StayValid_WhenEntitiesAreAddedMidIteration()
    {
        var store = NewStore(EntityStore.ChunkSize);
        var info = store.GetColumn<Info>();
        for (int i = 0; i < EntityStore.ChunkSize; i++) info[store.Create()] = new Info { NameSeed = (uint)i };

        var first = info.Chunk(0);                         // grab a span, then force several new chunks
        for (int i = 0; i < EntityStore.ChunkSize * 3; i++) store.Create();
        first[5].Age = 77;                                  // still points at live storage
        Assert.Equal(77, info.At(5).Age);
        Assert.Equal(4, info.ChunkCount);

        // Mid-iteration spawns are visible to later chunks of the same scan.
        int seen = 0;
        for (int k = 0; k < info.ChunkCount; k++) seen += info.Chunk(k).Length;
        Assert.Equal(store.HighWater, seen);
    }

    [Fact]
    public void KeyMap_IsIdempotentForOnDemandSpawns()
    {
        var store = NewStore();
        var map = new EntityKeyMap(2);
        ulong key = EntityKeyMap.MakeKey(42, 1234, 3);
        Assert.False(map.TryGetLive(store, key, out _));
        var id = store.Create(); map.Set(key, id);
        Assert.True(map.TryGetLive(store, key, out var again));
        Assert.Equal(id, again);

        store.Destroy(id);                                   // despawned: key resolves to a dead handle
        Assert.False(map.TryGetLive(store, key, out _));
        var respawn = store.Create(); map.Set(key, respawn); // same key overwrites in place
        Assert.Equal(1, map.Count);
        Assert.True(map.TryGetLive(store, key, out var live));
        Assert.Equal(respawn, live);
        Assert.NotEqual(EntityKeyMap.MakeKey(42, 1234, 4), key);
    }

    [Fact]
    public void KeyMap_SurvivesGrowthAndRemovals()
    {
        var store = NewStore();
        var map = new EntityKeyMap(4);
        var rng = new Random(3);
        var live = new Dictionary<ulong, EntityId>();
        for (int i = 0; i < 20_000; i++)
        {
            ulong key = (ulong)rng.Next(5000) + 1;
            if (rng.Next(3) == 0) { Assert.Equal(live.Remove(key), map.Remove(key)); }
            else { var id = store.Create(); map.Set(key, id); live[key] = id; }
        }
        Assert.Equal(live.Count, map.Count);
        foreach (var (key, id) in live)
        {
            Assert.True(map.TryGet(key, out var got));
            Assert.Equal(id, got);
        }
    }

    [Fact]
    public void KeyMap_Rebuild_RecoversFromKeyColumn()
    {
        var store = new EntityStore();
        var keys = store.AddColumn<ulong>();
        var map = new EntityKeyMap();
        var ids = new List<EntityId>();
        for (int i = 0; i < 3000; i++)
        {
            var id = store.Create();
            keys[id] = (ulong)(i + 1) * 1000003;
            ids.Add(id);
        }
        store.Destroy(ids[7]);
        map.Rebuild(store, keys);
        Assert.Equal(2999, map.Count);
        Assert.True(map.TryGetLive(store, 9UL * 1000003, out var id8));   // key of entity 8
        Assert.Equal(ids[8], id8);
        Assert.False(map.TryGetLive(store, 8UL * 1000003, out _));        // entity 7 was destroyed
    }

    private struct PlaceTile : ICellOf<Pos> { public int Cell(in Pos p) => p.Tile; }

    [Fact]
    public void CellIndex_RebuildFromColumn_MatchesSpanRebuild_AndSkipsDead()
    {
        var store = NewStore();
        var pos = store.GetColumn<Pos>();
        var ids = new List<EntityId>();
        var rng = new Random(5);
        for (int i = 0; i < 3000; i++)
        {
            var id = store.Create();
            pos[id] = new Pos { Tile = rng.Next(50) };
            ids.Add(id);
        }
        for (int i = 0; i < ids.Count; i += 3) store.Destroy(ids[i]);

        var viaColumn = new CellIndex();
        viaColumn.Rebuild(50, store, pos, new PlaceTile());

        var cells = new int[store.HighWater];
        for (int s = 0; s < cells.Length; s++) cells[s] = store.IsAliveSlot(s) ? pos.At(s).Tile : -1;
        var viaSpan = new CellIndex();
        viaSpan.Rebuild(50, cells);

        Assert.Equal(store.Count, viaColumn.Count);
        for (int c = 0; c < 50; c++) Assert.Equal(viaSpan.InCell(c).ToArray(), viaColumn.InCell(c).ToArray());

        long before = GC.GetAllocatedBytesForCurrentThread();
        viaColumn.Rebuild(50, store, pos, new PlaceTile());
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void CellIndex_BucketsSlots_AndSkipsUnplaced()
    {
        var index = new CellIndex();
        int[] cells = { 2, 0, 2, -1, 1, 2, 9, 0 };      // 9 is out of range for 3 cells
        index.Rebuild(3, cells);
        Assert.Equal(new[] { 1, 7 }, index.InCell(0).ToArray());
        Assert.Equal(new[] { 4 }, index.InCell(1).ToArray());
        Assert.Equal(new[] { 0, 2, 5 }, index.InCell(2).ToArray());
        Assert.True(index.InCell(7).IsEmpty);
        Assert.Equal(6, index.Count);

        index.Rebuild(3, new[] { 1, 1 });                // reuse shrinks cleanly
        Assert.True(index.InCell(0).IsEmpty);
        Assert.Equal(new[] { 0, 1 }, index.InCell(1).ToArray());
    }

    [Fact]
    public void CellIndex_Rebuild_AllocatesNothingOnceWarm()
    {
        const int n = 200_000, cellCount = 40_962;
        var cells = new int[n];
        var rng = new Random(1);
        for (int i = 0; i < n; i++) cells[i] = rng.Next(cellCount);
        var index = new CellIndex();
        index.Rebuild(cellCount, cells);               // warm-up sizes the buffers

        long before = GC.GetAllocatedBytesForCurrentThread();
        index.Rebuild(cellCount, cells);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(n, index.Count);
    }

    [Fact]
    public void CreateDestroy_AllocatesNothingOnceWarm()
    {
        var store = NewStore(1024);
        var ids = new EntityId[500];
        for (int i = 0; i < ids.Length; i++) ids[i] = store.Create();
        foreach (var id in ids) store.Destroy(id);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < ids.Length; i++) ids[i] = store.Create();
        foreach (var id in ids) store.Destroy(id);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
