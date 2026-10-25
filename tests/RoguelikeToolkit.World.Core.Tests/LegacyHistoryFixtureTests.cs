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
    [InlineData(1, 3, "F2C6CE26CD87A2BA0B8CD3B6504EF9A01DD524CBC7C53DB048619BF2E72AC5AC", "3DB8BF8FCF662510E30F8250E9D672C6F155CA343E12516862357608D9E67433")]
    [InlineData(2, 3, "0E79856771F8383F6AE76354AC6CD49C71F084AE815533C2B23F38483D9A4CCD", "C9ECB5F969E5D30C4C4DB70B91B828475B331289677AF69D219346A996B8F6AF")]
    [InlineData(3, 3, "8CC4DB066811C08034DC9E8BFFBE86F0507F1DE127B33F6C3F9AAFE618F9710C", "7B3A65C15112C5DF16B7EFFC6B2E8603B11E10DE347EA3D4122F65D1C25EC247")]
    [InlineData(4, 3, "32AE3C0619FD245C2C4F9340DD780B42D5EB02FCEA68F1C938D3D115AA619FE1", "BCA9910760171516262D1A9E22E45988E4062FB6BF1CB0AFE1F8E444EBF39845")]
    [InlineData(5, 3, "298441F771416B09AFF7C0FCE9D8B75202CAF30D81547060C5F666ABB5948E3C", "4A11FBD7731E5554970EB364E5E6D89E64A552E2F9771A4D2CDF2994C039828F")]
    [InlineData(1, 4, "7AC5174B76B4509AEE1507568D323E5CC271312797AEE81B49D4F4EA64FC0432", "0384486ACBAB5F68B8542AA2E70C839E78AE99E9598BC876D1D20E64470F43C9")]
    [InlineData(2, 4, "8B613EAF1A62D6C6E489B93D5BDE79325538AF5016C324FD297962EA8FE22F64", "CB986203AE4273BCBB5C00A432032442FBE483840E2472ED8A111180C54C96A2")]
    [InlineData(3, 4, "1D0D58A8080365D347F83901DAB390BEB8B33D89708DB65F4FA457EAC7B5D93F", "9E44DA9985F1E8887C2D6863A650C4098E2B924D31B920D1F87187AD1B1C5E2F")]
    [InlineData(4, 4, "DA715D0427EB7BB23CB12E24BF6823593571B3C4771F98FF4B81BD2BCA91EB71", "8681D3A16FE383F5E356D0622275A5284DB371EB287179CFFF8C397EA1EBCA9D")]
    [InlineData(5, 4, "AF966D68BD1F3084D7E6AB83DD48B313A3BC3A3D5CB0604D1E382E59560E9812", "908A369D38118085233876B3F3C7DCD29053A90F1619063A2C2E446304A913CC")]
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
