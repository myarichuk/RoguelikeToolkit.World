using System.Security.Cryptography;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// The history expansion's safety net (docs/HISTORY_EXPANSION_PLAN.md, X0): ten worlds pinned two ways.
/// <list type="bullet">
/// <item>The <see cref="HistoryProjection"/> hash pins what the legacy simulation decided. With every
/// <c>HistoryFeatures</c> flag off it must never move; a kernel or schema change that moves it changed behaviour.</item>
/// <item>The <see cref="HistoryCatalog.ToBytes"/> hash pins the encoding. It moves with a schema bump (re-pin it in that commit,
/// after checking the projection held).</item>
/// </list>
/// </summary>
public class LegacyHistoryFixtureTests
{
    [Theory]
    [InlineData(1, 3, "F2C6CE26CD87A2BA0B8CD3B6504EF9A01DD524CBC7C53DB048619BF2E72AC5AC", "D26FD68B29F2C380999BEF40D6C7CB283A46D999A3F6D7AB66B5D72C4D05AEAB")]
    [InlineData(2, 3, "0E79856771F8383F6AE76354AC6CD49C71F084AE815533C2B23F38483D9A4CCD", "C12F1CDCFDE7787724EBDE518C43B7BEA2928137C6CCB88C7C48120A31F18BCA")]
    [InlineData(3, 3, "8CC4DB066811C08034DC9E8BFFBE86F0507F1DE127B33F6C3F9AAFE618F9710C", "AF0549B5CBE8D0B70FBDABCED9F4542EC1967A5D0392BB941A8786CF49346C1C")]
    [InlineData(4, 3, "32AE3C0619FD245C2C4F9340DD780B42D5EB02FCEA68F1C938D3D115AA619FE1", "A6486068FDEC7A3E01287A5BA77323B4A4937241AE859725FCEC8458A0BD9A6E")]
    [InlineData(5, 3, "298441F771416B09AFF7C0FCE9D8B75202CAF30D81547060C5F666ABB5948E3C", "2C36A1165927F31C50502B6486B849904137EB4DC94F4BCF6E85646C19049A46")]
    [InlineData(1, 4, "7AC5174B76B4509AEE1507568D323E5CC271312797AEE81B49D4F4EA64FC0432", "8FEB5BEE79DE43BD5088F8C7392288A88D3382A26ECEC25F00F30EFE27EEAF1D")]
    [InlineData(2, 4, "8B613EAF1A62D6C6E489B93D5BDE79325538AF5016C324FD297962EA8FE22F64", "E92D24C2A0FE86D2C5D61F53CC0BBF81DD02791A3111F603A97612BE3E7D0C4E")]
    [InlineData(3, 4, "1D0D58A8080365D347F83901DAB390BEB8B33D89708DB65F4FA457EAC7B5D93F", "EE0AA77E69EBE085E092A660E5CCC72AD179E6E009C826D912D168313791B5B3")]
    [InlineData(4, 4, "DA715D0427EB7BB23CB12E24BF6823593571B3C4771F98FF4B81BD2BCA91EB71", "ACD6BD741D8CBE32329CB324B2830C58976F3716285F84AD22FE4CDCEB881E74")]
    [InlineData(5, 4, "AF966D68BD1F3084D7E6AB83DD48B313A3BC3A3D5CB0604D1E382E59560E9812", "290C8B97FC6465597F71B53EDC2B599C4292B596C06A56F8939EC4D431616665")]
    public void LegacyHistory_IsPinned(int seed, int size, string projection, string bytes)
    {
        using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        var history = world.History;
        string actualProjection = HistoryProjection.Hash(history);
        string actualBytes = Convert.ToHexString(SHA256.HashData(history.ToBytes()));
        Assert.True(projection == actualProjection && bytes == actualBytes,
            $"seed={seed} size={size}: projection {actualProjection} bytes {actualBytes}");
    }

    [Fact]
    public void Projection_IgnoresSchemaAndInterning_ButSeesDecisions()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(1).Build();
        var history = world.History;
        string before = HistoryProjection.Hash(history);

        // A save/load round trip renumbers nothing the projection reads.
        var loaded = HistoryCatalog.Load(new MemoryStream(history.ToBytes()));
        Assert.Equal(before, HistoryProjection.Hash(loaded));

        // Interning an unused name shifts later string ids in a fresh run, never the projection.
        history.Strings.Intern("x0-unused-name");
        Assert.Equal(before, HistoryProjection.Hash(history));

        // A real decision moves it.
        history.Polities[0].RulerGeneration++;
        Assert.NotEqual(before, HistoryProjection.Hash(history));
    }
}
