using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading;
using Jint;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Scripting.Jint;

/// <summary>
/// Limits for one untrusted script run. Defaults are deliberately small:
/// generation scripts loop over thousands of tiles, so a runaway must die in
/// seconds, not minutes.
/// </summary>
public sealed class JintStageLimits
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxStatements { get; init; } = 250_000;
    public long MemoryLimitBytes { get; init; } = 16_000_000;
    public int MaxRecursion { get; init; } = 256;
}

/// <summary>
/// Everything needed to build a <see cref="JintWorldStage"/>.
/// Reads/Writes/ReadsOptional are Core layer <see cref="Type"/>s
/// (e.g. <c>typeof(ElevationInfo)</c>) and join the same contract validation
/// as compiled C# stages: blind second writers throw, missing layers fail fast.
/// </summary>
public sealed class JintStageSpec
{
    public string Name { get; init; } = "jint-stage";
    public string Source { get; init; } = string.Empty;
    public int Order { get; init; }
    public Type[] Reads { get; init; } = Array.Empty<Type>();
    public Type[] Writes { get; init; } = Array.Empty<Type>();
    public Type[] ReadsOptional { get; init; } = Array.Empty<Type>();
    public int Seed { get; init; } = 42;
    public IReadOnlyDictionary<string, object?>? Params { get; init; }
    public JintStageLimits Limits { get; init; } = new();
}

/// <summary>
/// An untrusted JavaScript pipeline stage. The script must define a global
/// <c>execute()</c> function; the host calls it once per <c>Execute</c>.
/// Scripts see only the narrow tile helpers below — no CLR, no modules, no
/// <c>eval</c>, no IO — plus statement/memory/timeout budgets enforced by Jint.
/// Full file-system/network power is reserved for compiled C# plugins.
/// </summary>
public sealed class JintWorldStage : IWorldGeneratorStage, IDeclaredStage, IStageNamed, ISeededStage
{
    private readonly JintStageSpec _spec;
    private readonly HashSet<Type> _reads;
    private readonly HashSet<Type> _writes;
    private readonly HashSet<Type> _readsOptional;

    public string Name => _spec.Name;
    public int Order => _spec.Order;
    public Type[] Reads => _spec.Reads;
    public Type[] Writes => _spec.Writes;
    public Type[] ReadsOptional => _spec.ReadsOptional;
    public int Seed { get; set; }

    public JintWorldStage(JintStageSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Source))
            throw new ArgumentException("Jint stage source must not be empty.", nameof(spec));
        _reads = new HashSet<Type>(spec.Reads ?? Array.Empty<Type>());
        _writes = new HashSet<Type>(spec.Writes ?? Array.Empty<Type>());
        _readsOptional = new HashSet<Type>(spec.ReadsOptional ?? Array.Empty<Type>());
        Seed = spec.Seed;
    }

    public void Execute(WorldMap map)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        var store = map.DataStore;

        // Fail fast with the same RegisterLayer hint compiled stages get.
        foreach (var t in _reads)
            if (!store.IsLayerRegistered(t))
                throw new InvalidOperationException(
                    $"Jint stage '{Name}' reads layer '{t.Name}' but it is not registered. Call RegisterLayer<{t.Name}>() before Allocate().");
        foreach (var t in _writes)
            if (!store.IsLayerRegistered(t))
                throw new InvalidOperationException(
                    $"Jint stage '{Name}' writes layer '{t.Name}' but it is not registered. Call RegisterLayer<{t.Name}>() before Allocate().");

        var limits = _spec.Limits ?? new JintStageLimits();
        using var cts = new CancellationTokenSource(limits.Timeout + TimeSpan.FromSeconds(2));
        var token = cts.Token;
        int maxStatements = limits.MaxStatements;
        long memoryBytes = limits.MemoryLimitBytes;
        TimeSpan timeout = limits.Timeout;
        int maxRecursion = limits.MaxRecursion;

        // NOTE: never AllowClr here. Host delegates below are the entire API.
        var engine = new Engine(o =>
        {
            o.Strict(true);
            o.DisableStringCompilation(true);
            o.LimitRecursion(maxRecursion);
            o.MaxStatements(maxStatements);
            o.LimitMemory(memoryBytes);
            o.TimeoutInterval(timeout);
            o.CancellationToken(token);
        });

        string stageName = Name;
        int seed = Seed;
        int tileCount = store.TileCount;

        double ReadContinentality(int i) => GetLayerValue(
            store, typeof(TectonicPlate), i, _reads, _readsOptional, stageName,
            () => store.GetRef<TectonicPlate>(i).Continentality);
        double ReadOrogeny(int i) => GetLayerValue(
            store, typeof(TectonicPlate), i, _reads, _readsOptional, stageName,
            () => store.GetRef<TectonicPlate>(i).Orogeny);
        double ReadHeight(int i) => GetLayerValue(
            store, typeof(ElevationInfo), i, _reads, _readsOptional, stageName,
            () => store.GetRef<ElevationInfo>(i).Height);
        double ReadTemp(int i) => GetLayerValue(
            store, typeof(ClimateInfo), i, _reads, _readsOptional, stageName,
            () => store.GetRef<ClimateInfo>(i).Temperature);
        double ReadPrecip(int i) => GetLayerValue(
            store, typeof(ClimateInfo), i, _reads, _readsOptional, stageName,
            () => store.GetRef<ClimateInfo>(i).Precipitation);
        double ReadFlow(int i) => GetLayerValue(
            store, typeof(HydrologyInfo), i, _reads, _readsOptional, stageName,
            () => store.GetRef<HydrologyInfo>(i).Flow);
        double ReadSurface(int i) => GetLayerValue(
            store, typeof(HydrologyInfo), i, _reads, _readsOptional, stageName,
            () => store.GetRef<HydrologyInfo>(i).Surface);
        double ReadBiome(int i) => GetLayerValue(
            store, typeof(LocalMapInfo), i, _reads, _readsOptional, stageName,
            () => (double)store.GetRef<LocalMapInfo>(i).Biome);
        double ReadDanger(int i) => GetLayerValue(
            store, typeof(LocalMapInfo), i, _reads, _readsOptional, stageName,
            () => store.GetRef<LocalMapInfo>(i).DangerLevel);

        void WriteHeight(int i, double v)
        {
            RequireWrite(typeof(ElevationInfo), stageName, _writes);
            store.GetRef<ElevationInfo>(CheckIndex(i, tileCount)).Height = (float)v;
        }

        void WriteTemp(int i, double v)
        {
            RequireWrite(typeof(ClimateInfo), stageName, _writes);
            store.GetRef<ClimateInfo>(CheckIndex(i, tileCount)).Temperature = (float)v;
        }

        void WritePrecip(int i, double v)
        {
            RequireWrite(typeof(ClimateInfo), stageName, _writes);
            store.GetRef<ClimateInfo>(CheckIndex(i, tileCount)).Precipitation = (float)v;
        }

        void WriteBiome(int i, double v)
        {
            RequireWrite(typeof(LocalMapInfo), stageName, _writes);
            store.GetRef<LocalMapInfo>(CheckIndex(i, tileCount)).Biome = (BiomeType)CheckBiome((int)v);
        }

        void WriteDanger(int i, double v)
        {
            RequireWrite(typeof(LocalMapInfo), stageName, _writes);
            store.GetRef<LocalMapInfo>(CheckIndex(i, tileCount)).DangerLevel = (byte)CheckDanger((int)v);
        }

        double Rand01(int tile, int salt)
        {
            int t = CheckIndex(tile, tileCount);
            return Rng.Create(unchecked(seed * 7919 + t * 104729), salt).NextDouble();
        }

        engine.SetValue("TILE_COUNT", tileCount);
        engine.SetValue("SEED", seed);
        engine.SetValue("PARAMS", ToParamsObject(_spec.Params));
        engine.SetValue("BIOME", new
        {
            Ocean = (int)BiomeType.Ocean,
            Plains = (int)BiomeType.Plains,
            Desert = (int)BiomeType.Desert,
            Forest = (int)BiomeType.Forest,
            Mountain = (int)BiomeType.Mountain,
            Tundra = (int)BiomeType.Tundra,
            Jungle = (int)BiomeType.Jungle,
            Swamp = (int)BiomeType.Swamp,
            Glacier = (int)BiomeType.Glacier,
            Canyon = (int)BiomeType.Canyon,
        });
        engine.SetValue("lat", new Func<int, double>(i => store.GetGeoCoord(CheckIndex(i, tileCount)).Latitude));
        engine.SetValue("lon", new Func<int, double>(i => store.GetGeoCoord(CheckIndex(i, tileCount)).Longitude));
        engine.SetValue("continentality", new Func<int, double>(ReadContinentality));
        engine.SetValue("orogeny", new Func<int, double>(ReadOrogeny));
        engine.SetValue("height", new Func<int, double>(ReadHeight));
        engine.SetValue("temp", new Func<int, double>(ReadTemp));
        engine.SetValue("precip", new Func<int, double>(ReadPrecip));
        engine.SetValue("flow", new Func<int, double>(ReadFlow));
        engine.SetValue("surface", new Func<int, double>(ReadSurface));
        engine.SetValue("biome", new Func<int, double>(ReadBiome));
        engine.SetValue("danger", new Func<int, double>(ReadDanger));
        engine.SetValue("setHeight", new Action<int, double>(WriteHeight));
        engine.SetValue("setTemp", new Action<int, double>(WriteTemp));
        engine.SetValue("setPrecip", new Action<int, double>(WritePrecip));
        engine.SetValue("setBiome", new Action<int, double>(WriteBiome));
        engine.SetValue("setDanger", new Action<int, double>(WriteDanger));
        engine.SetValue("rand01", new Func<int, int, double>(Rand01));

        try
        {
            engine.Execute(_spec.Source);
            var fn = engine.GetValue("execute");
            if (!fn.IsObject() || fn.AsObject() is not global::Jint.Native.Function.Function function)
                throw new InvalidOperationException(
                    $"Jint stage '{stageName}' must define a global function execute().");
            engine.Invoke(function);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JintException || ex is TimeoutException || ex is OperationCanceledException)
        {
            throw new InvalidOperationException($"Jint stage '{stageName}' failed: {TrimMessage(ex)}", ex);
        }
    }

    private static double GetLayerValue(
        WorldDataStore store, Type layer, int index,
        HashSet<Type> reads, HashSet<Type> readsOptional,
        string stageName, Func<double> read)
    {
        int i = CheckIndex(index, store.TileCount);
        if (store.IsLayerRegistered(layer))
            return read();
        if (readsOptional.Contains(layer))
            return 0.0;
        if (reads.Contains(layer))
            throw new InvalidOperationException(
                $"Jint stage '{stageName}' reads layer '{layer.Name}' but it is not registered. Call RegisterLayer<{layer.Name}>() before Allocate().");
        throw new InvalidOperationException(
            $"Jint stage '{stageName}' reads layer '{layer.Name}' without declaring it in Reads/ReadsOptional.");
    }

    private static void RequireWrite(Type layer, string stageName, HashSet<Type> writes)
    {
        if (!writes.Contains(layer))
            throw new InvalidOperationException(
                $"Jint stage '{stageName}' writes layer '{layer.Name}' without declaring it in Writes.");
    }

    private static int CheckIndex(int i, int tileCount)
    {
        if ((uint)i >= (uint)tileCount)
            throw new InvalidOperationException($"Tile index {i} is out of range (0..{tileCount - 1}).");
        return i;
    }

    private static int CheckBiome(int v)
    {
        if (v < 0 || v > 9)
            throw new InvalidOperationException($"Biome value {v} is out of range (0..9, see BIOME.*).");
        return v;
    }

    private static int CheckDanger(int v)
    {
        if (v < 0 || v > 255)
            throw new InvalidOperationException($"Danger value {v} is out of range (0..255).");
        return v;
    }

    private static string TrimMessage(Exception ex)
    {
        string msg = ex.Message ?? ex.GetType().Name;
        int nl = msg.IndexOf('\n');
        if (nl >= 0) msg = msg.Substring(0, nl);
        return msg.Length > 300 ? msg.Substring(0, 300) + "…" : msg;
    }

    private static object ToParamsObject(IReadOnlyDictionary<string, object?>? @params)
    {
        var expando = new ExpandoObject() as IDictionary<string, object?>;
        if (@params != null)
            foreach (var kv in @params)
                expando[kv.Key] = kv.Value;
        return expando;
    }
}
