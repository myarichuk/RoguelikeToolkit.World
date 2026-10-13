using RoguelikeToolkit.World.Core.Entities;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Snapshots and deltas are untrusted bytes: a bad one must throw and leave the store exactly as it was.</summary>
public sealed class EntityStoreHostileInputTests
{
    private struct Pos { public int Tile; public ushort X, Y; }

    private const int SnapshotHeader = 40;
    private const int DeltaHeader = 48;

    private static EntityStore NewStore()
    {
        var s = new EntityStore(0);
        s.AddColumn<Pos>();
        return s;
    }

    // Three entities, the middle two destroyed, so the free list holds slots 1 and 2 (LIFO: 2 is popped first).
    private static EntityStore WithFreeList(out EntityId survivor)
    {
        var s = NewStore();
        survivor = s.Create();
        var b = s.Create(); var c = s.Create();
        s.Destroy(b); s.Destroy(c);
        return s;
    }

    private static byte[] Full(EntityStore s) { using var ms = new MemoryStream(); s.Save(ms); return ms.ToArray(); }
    private static byte[] Delta(EntityStore s) { using var ms = new MemoryStream(); s.SaveDelta(ms); return ms.ToArray(); }

    private static void Put(byte[] b, int offset, int value) => BitConverter.TryWriteBytes(b.AsSpan(offset, 4), value);
    private static void Put(byte[] b, int offset, ulong value) => BitConverter.TryWriteBytes(b.AsSpan(offset, 8), value);

    // Snapshot layout: 40 header | highWater*4 generations | freeCount*4 free list | columns.
    private static int FreeOffset(int highWater) => SnapshotHeader + highWater * 4;

    private static void AssertUntouched(EntityStore s, EntityId survivor)
    {
        Assert.Equal(1, s.Count);
        Assert.Equal(3, s.HighWater);
        Assert.True(s.IsAlive(survivor));
        // The free list still hands out the original slots, each exactly once.
        var x = s.Create(); var y = s.Create();
        Assert.Equal(new[] { 1, 2 }, new[] { x.Index, y.Index }.OrderBy(i => i));
    }

    [Theory]
    [InlineData(1, 1)]    // duplicate: two live entities would alias slot 1
    [InlineData(1, -1)]   // negative: crashes GenerationAt
    [InlineData(1, 99)]   // beyond the high-water mark
    [InlineData(1, 0)]    // slot 0 is alive
    public void Load_ForgedFreeList_Throws_AndLeavesStoreUnchanged(int index, int forged)
    {
        var src = WithFreeList(out _);
        var bytes = Full(src);
        Put(bytes, FreeOffset(3) + index * 4, forged);

        var dst = WithFreeList(out var survivor);
        Assert.Throws<InvalidDataException>(() => dst.Load(new MemoryStream(bytes)));
        AssertUntouched(dst, survivor);
    }

    [Fact]
    public void Load_AliveCountDisagreeingWithGenerations_Throws()
    {
        var bytes = Full(WithFreeList(out _));
        // The header stays self-consistent (alive 1 + free 2 = 3) but slot 0's generation is made even, so no slot is alive.
        Put(bytes, SnapshotHeader, 2);
        var dst = WithFreeList(out var survivor);
        Assert.Throws<InvalidDataException>(() => dst.Load(new MemoryStream(bytes)));
        AssertUntouched(dst, survivor);
    }

    [Fact]
    public void Load_HeaderThatDoesNotAddUp_Throws()
    {
        var bytes = Full(WithFreeList(out _));
        Put(bytes, 16, 2);    // alive + freeCount != highWater
        Assert.Throws<InvalidDataException>(() => NewStore().Load(new MemoryStream(bytes)));
    }

    [Fact]
    public void Load_HugeHeaderWithNoPayload_FailsOnTheShortStream_NotInAnAllocation()
    {
        var bytes = Full(WithFreeList(out _)).AsSpan(0, SnapshotHeader).ToArray();
        Put(bytes, 8, int.MaxValue);              // highWater
        Put(bytes, 12, int.MaxValue / 2);         // freeCount
        Put(bytes, 16, int.MaxValue - int.MaxValue / 2);
        Assert.Throws<EndOfStreamException>(() => NewStore().Load(new MemoryStream(bytes)));
    }

    [Fact]
    public void Load_ValidSnapshotWithLargeFreeList_RoundTrips()
    {
        var src = NewStore();
        var ids = Enumerable.Range(0, 40_000).Select(_ => src.Create()).ToList();
        foreach (var id in ids.Take(39_990)) src.Destroy(id);   // free list spans several read blocks

        var dst = NewStore();
        dst.Load(new MemoryStream(Full(src)));
        Assert.Equal(10, dst.Count);
        var seen = new HashSet<int>();
        for (int i = 0; i < 39_990; i++) Assert.True(seen.Add(dst.Create().Index));
        Assert.Equal(40_000, dst.HighWater);
    }

    private static (EntityStore dst, byte[] delta, EntityId survivor) DeltaFixture()
    {
        var src = WithFreeList(out var survivor);
        var dst = NewStore();
        dst.Load(new MemoryStream(Full(src)));
        src.Create();                                  // touches the free list and a generation
        return (dst, Delta(src), survivor);
    }

    // Delta layout: 48 header | freeCount*4 free list | gen chunk list | columns.
    [Fact]
    public void LoadDelta_SequenceThatSkipsAhead_Throws()
    {
        var (dst, delta, survivor) = DeltaFixture();
        Put(delta, 24, 5ul);                           // newSeq != baseSeq + 1
        Assert.Throws<InvalidDataException>(() => dst.LoadDelta(new MemoryStream(delta)));
        AssertUntouched(dst, survivor);
    }

    [Theory]
    [InlineData(0, 0)]    // slot 0 is alive
    [InlineData(0, -5)]
    [InlineData(0, 77)]
    public void LoadDelta_ForgedFreeList_Throws_AndLeavesStoreUnchanged(int index, int forged)
    {
        var (dst, delta, survivor) = DeltaFixture();
        // After src.Create() the free list is [1] (slot 2 was reused); freeCount=1, so only index 0 exists.
        Assert.Equal(1, BitConverter.ToInt32(delta.AsSpan(36, 4)));
        Put(delta, DeltaHeader + index * 4, forged);
        Assert.Throws<InvalidDataException>(() => dst.LoadDelta(new MemoryStream(delta)));
        AssertUntouched(dst, survivor);
    }

    [Fact]
    public void LoadDelta_ClaimingGrowthWithoutThePayload_Throws_AndAllocatesNothing()
    {
        var (dst, delta, survivor) = DeltaFixture();
        Put(delta, 32, 1 << 24);                       // highWater far past the data carried
        Put(delta, 36, 0);                             // freeCount
        Put(delta, 40, 1 << 24);                       // alive
        // Rebuild the body as an empty delta so nothing in the payload covers the new chunks.
        var forged = delta.AsSpan(0, DeltaHeader).ToArray().Concat(new byte[4]).ToArray();
        Assert.ThrowsAny<Exception>(() => dst.LoadDelta(new MemoryStream(forged)));
        AssertUntouched(dst, survivor);
    }
}
