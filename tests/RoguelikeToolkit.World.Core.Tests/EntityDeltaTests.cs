using RoguelikeToolkit.World.Core.Entities;

namespace RoguelikeToolkit.World.Core.Tests;

public sealed class EntityDeltaTests
{
    private struct Pos { public int Tile; public ushort X, Y; }
    private struct Info { public ushort Species; public byte Age; public uint NameSeed; }

    private static EntityStore NewStore(int capacity = 0)
    {
        var s = new EntityStore(capacity);
        s.AddColumn<Pos>();
        s.AddColumn<Info>();
        return s;
    }

    private static byte[] Full(EntityStore s) { using var ms = new MemoryStream(); s.Save(ms); return ms.ToArray(); }
    private static byte[] Delta(EntityStore s) { using var ms = new MemoryStream(); s.SaveDelta(ms); return ms.ToArray(); }

    private static void AssertSame(EntityStore a, EntityStore b)
    {
        Assert.Equal(Full(a).Length, Full(b).Length);
        Assert.Equal(a.Count, b.Count);
        Assert.Equal(a.HighWater, b.HighWater);
        for (int slot = 0; slot < a.HighWater; slot++)
        {
            Assert.Equal(a.IsAliveSlot(slot), b.IsAliveSlot(slot));
            if (!a.IsAliveSlot(slot)) continue;
            Assert.Equal(a.HandleOf(slot), b.HandleOf(slot));
            Assert.Equal(a.GetColumn<Pos>().At(slot).Tile, b.GetColumn<Pos>().At(slot).Tile);
            Assert.Equal(a.GetColumn<Info>().At(slot).NameSeed, b.GetColumn<Info>().At(slot).NameSeed);
        }
    }

    [Fact]
    public void Delta_AfterGrowthEditsAndDestroys_ReproducesStore()
    {
        var src = NewStore();
        var pos = src.GetColumn<Pos>();
        var info = src.GetColumn<Info>();
        var ids = Enumerable.Range(0, 5000).Select(_ => src.Create()).ToList();
        for (int i = 0; i < ids.Count; i++) { pos[ids[i]].Tile = i; info[ids[i]].NameSeed = (uint)i * 7; }

        var dst = NewStore();
        dst.Load(new MemoryStream(Full(src)));

        // Step 1: spawn into a new chunk, edit one old entity, destroy one.
        for (int i = 0; i < 1500; i++) { var id = src.Create(); pos[id].Tile = 9000 + i; info[id].NameSeed = 1; }
        pos[ids[42]].Tile = -1;
        src.Destroy(ids[4000]);
        var d1 = Delta(src);
        // Step 2: reuse the freed slot, touch another entity.
        var reused = src.Create();
        pos[reused].Tile = 777;
        info[ids[10]].Age = 3;
        var d2 = Delta(src);

        dst.LoadDelta(new MemoryStream(d1));
        dst.LoadDelta(new MemoryStream(d2));
        AssertSame(src, dst);
        Assert.Equal(-1, dst.GetColumn<Pos>()[ids[42]].Tile);
        Assert.False(dst.IsAlive(ids[4000]));
    }

    [Fact]
    public void Delta_IsProportionalToWhatChanged()
    {
        var src = NewStore();
        var pos = src.GetColumn<Pos>();
        var ids = Enumerable.Range(0, 20000).Select(_ => src.Create()).ToList();
        int fullSize = Full(src).Length;

        Assert.True(Delta(src).Length < 200);            // nothing changed: header only (+ empty lists)
        pos[ids[5]].Tile = 1;                            // one chunk of one column
        int oneChunk = Delta(src).Length;
        Assert.InRange(oneChunk, 1, fullSize / 10);
        Assert.True(Delta(src).Length < 200);            // the edit was consumed by the previous delta
    }

    [Fact]
    public void LoadDelta_RejectsWrongChainOrOrder_AndLeavesStoreIntact()
    {
        var src = NewStore();
        var pos = src.GetColumn<Pos>();
        var a = src.Create();
        var dst = NewStore();
        dst.Load(new MemoryStream(Full(src)));

        pos[a].Tile = 1; var d1 = Delta(src);
        pos[a].Tile = 2; var d2 = Delta(src);

        Assert.Throws<InvalidDataException>(() => dst.LoadDelta(new MemoryStream(d2)));   // skipped d1
        Assert.Equal(0, dst.GetColumn<Pos>()[a].Tile);

        var stranger = NewStore();
        stranger.Create();
        stranger.Load(new MemoryStream(Full(stranger)));
        Assert.Throws<InvalidDataException>(() => stranger.LoadDelta(new MemoryStream(d1))); // other chain

        dst.LoadDelta(new MemoryStream(d1));
        dst.LoadDelta(new MemoryStream(d2));
        Assert.Equal(2, dst.GetColumn<Pos>()[a].Tile);
    }

    [Fact]
    public void SaveDelta_WithoutBaseline_Throws()
    {
        var s = NewStore();
        Assert.False(s.CanSaveDelta);
        Assert.Throws<InvalidOperationException>(() => s.SaveDelta(new MemoryStream()));
    }

    [Fact]
    public void FullSaveStartsNewChain_OldDeltasNoLongerApply()
    {
        var src = NewStore();
        var a = src.Create();
        var baseline = Full(src);
        src.GetColumn<Pos>()[a].Tile = 5;
        var oldDelta = Delta(src);
        var rebase = Full(src);                     // new chain

        var dst = NewStore();
        dst.Load(new MemoryStream(rebase));
        Assert.Throws<InvalidDataException>(() => dst.LoadDelta(new MemoryStream(oldDelta)));
        var again = NewStore();
        again.Load(new MemoryStream(baseline));
        again.LoadDelta(new MemoryStream(oldDelta));
        Assert.Equal(5, again.GetColumn<Pos>()[a].Tile);
    }
}
