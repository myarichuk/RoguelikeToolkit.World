using System.Reflection;
using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Generation;

/// <summary>
/// An ordered list of <see cref="IWorldGeneratorStage"/>s that generates a world. Stages sort by their declared order
/// (undeclared stages run last) and their layer contracts are validated; at the start of a run, layers whose first writer
/// does not read them are cleared, so a re-run never sees the previous run's output.
/// </summary>
/// <remarks>Not thread-safe: the ordering and validation caches are rebuilt unsynchronised. Disposing it disposes stages that implement <see cref="IDisposable"/> and unloads plugin contexts.</remarks>
public class WorldGenerationPipeline : IDisposable
{
    private readonly List<IWorldGeneratorStage> _stages = new();
#if NET
    private readonly List<PluginLoadContext> _pluginContexts = new();
#endif
    // Cached execution order + contract validation. Rebuilt only when the
    // stage set changes, so repeated Execute calls stay allocation-free and
    // keep the zero-GC generation guarantee for hot paths.
    private readonly List<IWorldGeneratorStage> _orderedCache = new();
    // Declared (stage name, written layer) pairs, rebuilt with the order
    // cache so steady-state Execute validates without reflection.
    private readonly List<(string Stage, Type Layer)> _writesCache = new();
    // Declared required Reads per stage, rebuilt with the order cache so
    // steady-state Execute validates without reflection (zero-GC guarantee).
    private readonly List<(string Stage, Type Layer)> _readsCache = new();
    // Layers cleared before any stage runs: each declared layer's first writer
    // that does not read it. Re-running the pipeline on a used map therefore
    // never lets a stage observe the previous run's output (e.g. climate seeing
    // stale hydrology), and results depend only on seed and configuration.
    private readonly List<Type> _clearCache = new();
    // Per-start-order clear lists for ExecuteFrom, built lazily and dropped with the other caches.
    private readonly Dictionary<int, List<Type>> _clearFromCache = new();
    // Order of each entry in _orderedCache, resolved once: StageMetadata.GetOrder may reflect, and
    // the steady-state Execute loop must not (zero-GC guarantee).
    private readonly List<int> _orderOfCache = new();
    private bool _cacheDirty = true;

    /// <summary>The registered stages in registration order (not execution order).</summary>
    public IReadOnlyList<IWorldGeneratorStage> Stages => _stages;

    /// <summary>Appends <paramref name="stage"/>; the next run re-sorts and re-validates.</summary>
    /// <param name="stage">Stage to add. Its order comes from its attribute or <see cref="IDeclaredStage"/>.</param>
    public void AddStage(IWorldGeneratorStage stage)
    {
        _stages.Add(stage);
        _cacheDirty = true;
    }

    /// <summary>Removes every stage, disposing those that are <see cref="IDisposable"/>, and unloads plugin assembly contexts (.NET only; unloading is best-effort).</summary>
    public void ResetStages()
    {
        foreach (var stage in _stages.OfType<IDisposable>())
        {
            stage.Dispose();
        }

        _stages.Clear();
        _readsCache.Clear();
        _orderedCache.Clear();
        _writesCache.Clear();
        _clearCache.Clear();
        _clearFromCache.Clear();
        _orderOfCache.Clear();
#if NET
        foreach (var ctx in _pluginContexts)
        {
            try { ctx.Unload(); } catch { /* cooperative unload is best-effort */ }
        }
        _pluginContexts.Clear();
#endif
        _cacheDirty = true;
    }

    /// <summary>
    /// Discovers and loads IWorldGeneratorStage implementations with
    /// <see cref="WorldGeneratorStageAttribute"/> (or <see cref="IDeclaredStage"/>
    /// for runtime stages) from the current assembly and any .dll files in the
    /// specified directory. Compiled C# plugins are full-trust: only load DLLs
    /// you trust. Each plugin file loads into its own collectible
    /// <c>PluginLoadContext</c> (.NET only) (shared Core contracts, plugin-local
    /// dependencies) and unloads on <see cref="ResetStages"/>.
    /// By default, discovered stages are merged with existing stages and matching stage types are skipped.
    /// Throws InvalidOperationException if duplicate Order values are found.
    /// Throws AggregateException when assembly/stage load failures occur and no diagnostics callback is provided.
    /// </summary>
    public void Discover(string? pluginDirectory = null, Action<StageDiscoveryDiagnostic>? onDiagnostic = null)
    {
        var diagnostics = new List<StageDiscoveryDiagnostic>();
        var assemblies = new List<Assembly> { Assembly.GetExecutingAssembly() };

        if (!string.IsNullOrWhiteSpace(pluginDirectory) && Directory.Exists(pluginDirectory))
        {
#if NET
            var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories);
            Array.Sort(dllFiles, StringComparer.Ordinal);
            foreach (var file in dllFiles)
            {
                // Skip the Core assembly itself when the plugin dir is the app dir.
                try
                {
                    if (string.Equals(
                        Path.GetFullPath(file),
                        Path.GetFullPath(Assembly.GetExecutingAssembly().Location),
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch { /* path compare is best-effort; fall through to load */ }

                // Only assemblies that reference the Core contracts can contain
                // stages. Plugin folders also hold dependency DLLs (and sometimes
                // the host's own); loading those as plugins pins them in a
                // context for nothing and surfaces their load failures as noise.
                if (!PluginLoadContext.MayContainStages(file))
                    continue;

                PluginLoadContext? ctx = null;
                try
                {
                    ctx = new PluginLoadContext(file);
                    assemblies.Add(ctx.LoadPluginAssembly(file));
                    _pluginContexts.Add(ctx);
                }
                catch (Exception ex)
                {
                    // A context that never made it into _pluginContexts would
                    // otherwise stay alive (and keep the DLL locked) forever.
                    try { ctx?.Unload(); } catch { /* best-effort */ }
                    diagnostics.Add(new StageDiscoveryDiagnostic($"Failed to load plugin assembly '{file}'.", ex));
                }
            }
        #else
            diagnostics.Add(new StageDiscoveryDiagnostic(
                $"Loading plugin DLLs from '{pluginDirectory}' needs .NET's AssemblyLoadContext. On this runtime (Unity Mono/IL2CPP) " +
                "compile plugins into the player and pass their assemblies to DiscoverFrom(...).",
                new PlatformNotSupportedException("Runtime plugin loading is not available on this runtime.")));
#endif
        }

        DiscoverCore(assemblies, diagnostics, onDiagnostic);
    }

    /// <summary>
    /// Discovers stages (same rules as <see cref="Discover(string?, Action{StageDiscoveryDiagnostic}?)"/>) from Core plus
    /// already-loaded <paramref name="pluginAssemblies"/>. This is the plugin path for runtimes that cannot load
    /// DLLs at runtime (Unity IL2CPP): plugins are compiled into the build and handed over as assemblies.
    /// No load context is created, so nothing is unloaded on <see cref="ResetStages"/>.
    /// </summary>
    public void DiscoverFrom(IEnumerable<Assembly> pluginAssemblies, Action<StageDiscoveryDiagnostic>? onDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(pluginAssemblies);

        var assemblies = new List<Assembly> { Assembly.GetExecutingAssembly() };
        foreach (var assembly in pluginAssemblies)
        {
            if (assembly != null && !assemblies.Contains(assembly))
                assemblies.Add(assembly);
        }

        DiscoverCore(assemblies, new List<StageDiscoveryDiagnostic>(), onDiagnostic);
    }

    // Built-in stages are only constructed here, through Activator: tell trimming linkers (ILLink/NativeAOT, and
    // UnityLinker, which shares that codebase) to keep their parameterless constructors. [Preserve] on the types
    // covers UnityLinker's own convention.
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(TectonicPlateGenerationStage))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ElevationGenerationStage))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ClimateStage))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ErosionGenerationStage))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(HydrologyStage))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(BiomeGenerationStage))]
    private void DiscoverCore(List<Assembly> assemblies, List<StageDiscoveryDiagnostic> diagnostics, Action<StageDiscoveryDiagnostic>? onDiagnostic)
    {
        var stageTypes = assemblies
            .SelectMany(a => {
                try { return (IEnumerable<Type>)a.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    // Keep the types that did load, but never swallow why the rest did not.
                    var detail = string.Join("; ", e.LoaderExceptions.Where(x => x != null).Select(x => x!.Message).Distinct());
                    diagnostics.Add(new StageDiscoveryDiagnostic(
                        $"Some types in assembly '{a.GetName().Name}' failed to load: {detail}", e));
                    return e.Types.OfType<Type>();
                }
            })
            .Where(t => typeof(IWorldGeneratorStage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<WorldGeneratorStageAttribute>() })
            .Where(t => t.Attribute != null)
            .ToList();

        // Duplicate-order check: attribute types use distinct-type semantics
        // (re-discovering an already-added type is a merge-skip, not a
        // duplicate), plus any already-added runtime stages (IDeclaredStage,
        // e.g. Jint) participate by instance order.
        var knownTypeOrders = _stages.Select(stage => stage.GetType())
            .Concat(stageTypes.Select(stage => stage.Type))
            .Distinct()
            .Select(type => type.GetCustomAttribute<WorldGeneratorStageAttribute>())
            .Where(attr => attr != null)
            .Select(attr => attr!.Order)
            .ToList();
        var declaredInstanceOrders = _stages
            .OfType<IDeclaredStage>()
            .Where(s => StageMetadata.HasDeclaration((IWorldGeneratorStage)s))
            .Select(s => s.Order)
            .ToList();

        var duplicateOrders = knownTypeOrders.Concat(declaredInstanceOrders)
            .GroupBy(o => o)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateOrders.Any())
        {
            throw new InvalidOperationException($"Duplicate Order values found for WorldGeneratorStageAttribute: {string.Join(", ", duplicateOrders)}");
        }

        var existingStageTypes = _stages.Select(stage => stage.GetType()).ToHashSet();

        var orderedTypes = stageTypes
            .Where(stage => !existingStageTypes.Contains(stage.Type))
            .OrderBy(t => t.Attribute!.Order)
            .ToList();

        foreach (var typeInfo in orderedTypes)
        {
            try
            {
                var instance = (IWorldGeneratorStage)Activator.CreateInstance(typeInfo.Type)!;
                _stages.Add(instance);
                _cacheDirty = true;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new StageDiscoveryDiagnostic($"Failed to instantiate pipeline stage '{typeInfo.Type.FullName}'.", ex));
            }
        }

        if (diagnostics.Count > 0)
        {
            if (onDiagnostic != null)
            {
                foreach (var diagnostic in diagnostics)
                {
                    onDiagnostic(diagnostic);
                }
            }
            else
            {
                throw new AggregateException("World generation stage discovery failed.", diagnostics.Select(d => d.Exception));
            }
        }
    }

    /// <summary>Calls <see cref="ResetStages"/> then <see cref="Discover(string?, Action{StageDiscoveryDiagnostic}?)"/>: replaces every registered stage, including hand-added ones, with the discovered set.</summary>
    /// <param name="pluginDirectory">Directory searched recursively for plugin DLLs, or <see langword="null"/> for built-in stages only.</param>
    /// <param name="onDiagnostic">Receives load failures; when <see langword="null"/> they are thrown as an <see cref="AggregateException"/>.</param>
    public void DiscoverAndReplace(string? pluginDirectory = null, Action<StageDiscoveryDiagnostic>? onDiagnostic = null)
    {
        ResetStages();
        Discover(pluginDirectory, onDiagnostic);
    }


    /// <summary>Same as <see cref="ResetStages"/>.</summary>
    public void Dispose()
    {
        ResetStages();
    }

    /// <summary>
    /// Validates declared stage contracts (Reads/Writes). Stages without
    /// declarations are skipped. Throws InvalidOperationException on collision.
    /// </summary>
    public void ValidateContracts()
    {
        StageContractValidator.ThrowOnConflicts(_stages);
    }

    /// <summary>
    /// Validates contracts and that every declared required Reads/Writes type
    /// is registered in the map's store. ReadsOptional types are skipped: by
    /// design they may be absent or written later. Throws
    /// InvalidOperationException otherwise.
    /// </summary>
    public void ValidateContracts(WorldMap map)
    {
        ValidateContracts();
        foreach (var stage in _stages)
        {
            if (!StageMetadata.HasDeclaration(stage)) continue;
            foreach (var t in StageMetadata.GetReads(stage).Concat(StageMetadata.GetWrites(stage)).Distinct())
            {
                if (!map.DataStore.IsLayerRegistered(t))
                    throw new InvalidOperationException(
                        $"Stage '{StageMetadata.DisplayName(stage)}' declares layer '{t.Name}' but it is not registered. Call RegisterLayer<{t.Name}>() before Allocate().");
            }
        }
    }

    /// <summary>Runs every stage in order against <paramref name="map"/> after validating contracts and clearing layers.</summary>
    /// <param name="map">Map whose store has every declared layer registered.</param>
    /// <exception cref="InvalidOperationException">A contract conflict, or a declared layer that is not registered in the map's store.</exception>
    public void Execute(WorldMap map) => Execute(map, int.MinValue, int.MaxValue, null);

    /// <summary>
    /// Re-runs the pipeline from the first stage whose order is at least <paramref name="fromOrder"/>,
    /// keeping every layer the skipped stages wrote. This is the incremental path behind
    /// <see cref="World.Recompute"/>: after a hand edit the expensive plate simulation (order 10) is
    /// not repeated, only the stages downstream of the changed input. Layers are cleared exactly as a
    /// full run would clear them, restricted to the stages that run, so the result is identical to a
    /// full <see cref="Execute(WorldMap)"/> on the same seed and inputs.
    /// </summary>
    public void ExecuteFrom(WorldMap map, int fromOrder) => Execute(map, fromOrder, int.MaxValue, null);
    /// <summary>
    /// Runs the stages whose order lies in [<paramref name="fromOrder"/>, <paramref name="toOrder"/>] against <paramref name="map"/>,
    /// keeping every layer the out-of-range stages wrote. This is how <see cref="World.RegenerateHistory"/> re-runs only the history
    /// band: the physics layers survive untouched. Layers are cleared exactly as a full run would clear them, restricted to the stages
    /// that run. Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="toOrder"/> is below <paramref name="fromOrder"/>.
    /// </summary>
    public void ExecuteRange(WorldMap map, int fromOrder, int toOrder) => Execute(map, CheckedRange(fromOrder, toOrder), toOrder, null);
    /// <summary>
    /// Runs the stages whose order lies in [<paramref name="fromOrder"/>, <paramref name="toOrder"/>] except the stages
    /// <paramref name="skip"/> selects, keeping every layer the skipped stages wrote. Throws <see cref="ArgumentOutOfRangeException"/>
    /// when <paramref name="toOrder"/> is below <paramref name="fromOrder"/> and <see cref="ArgumentNullException"/> when
    /// <paramref name="skip"/> is null.
    /// </summary>
    public void ExecuteRange(WorldMap map, int fromOrder, int toOrder, Predicate<IWorldGeneratorStage> skip)
    {
        ArgumentNullException.ThrowIfNull(skip);
        Execute(map, CheckedRange(fromOrder, toOrder), toOrder, skip);
    }
    /// <summary>
    /// Re-runs the pipeline from the first stage whose order is at least <paramref name="fromOrder"/>, except the stages
    /// <paramref name="skip"/> selects. Layers written only by skipped stages are kept, so skipping the history band (as
    /// <see cref="World.Recompute"/> does) leaves the history catalog's tile inputs intact. Throws <see cref="ArgumentNullException"/>
    /// when <paramref name="skip"/> is null.
    /// </summary>
    public void ExecuteFromExcept(WorldMap map, int fromOrder, Predicate<IWorldGeneratorStage> skip)
    {
        ArgumentNullException.ThrowIfNull(skip);
        Execute(map, fromOrder, int.MaxValue, skip);
    }

    private void Execute(WorldMap map, int fromOrder, int toOrder, Predicate<IWorldGeneratorStage>? skip)
    {
        // Fail fast on plugin collisions before mutating anything. Ordering +
        // validation are cached so steady-state Execute stays allocation-free.
        if (_cacheDirty)
        {
            StageContractValidator.ThrowOnConflicts(_stages);
            _orderedCache.Clear();
            _readsCache.Clear();
            _orderedCache.AddRange(_stages.OrderBy(StageMetadata.GetOrder));
            _writesCache.Clear();
            _clearCache.Clear();
            _clearFromCache.Clear();
            _orderOfCache.Clear();
            var written = new HashSet<Type>();
            foreach (var stage in _orderedCache)
            {
                _orderOfCache.Add(StageMetadata.GetOrder(stage));
                var stageReads = StageMetadata.GetReads(stage);
                foreach (var w in StageMetadata.GetWrites(stage))
                {
                    if (written.Add(w) && Array.IndexOf(stageReads, w) < 0)
                        _clearCache.Add(w);
                }
                foreach (var r in StageMetadata.GetReads(stage))
                    _readsCache.Add((StageMetadata.DisplayName(stage), r));
                foreach (var w in StageMetadata.GetWrites(stage))
                    _writesCache.Add((StageMetadata.DisplayName(stage), w));
            }
            _cacheDirty = false;
        }
        // Fail fast on missing declared layers (Writes plus required Reads; ReadsOptional
        // layers stay allowed-absent). Without this the first stage
        // touching the layer throws a bare ArgumentException from GetSpan.
        foreach (var (readStage, readLayer) in _readsCache)
        {
            if (!map.DataStore.IsLayerRegistered(readLayer))
                throw new InvalidOperationException(
                    $"Stage '{readStage}' reads layer '{readLayer.Name}' but it is not registered. Call RegisterLayer<{readLayer.Name}>() before Allocate().");
        }
        foreach (var (stageName, layer) in _writesCache)
        {
            if (!map.DataStore.IsLayerRegistered(layer))
                throw new InvalidOperationException(
                    $"Stage '{stageName}' writes layer '{layer.Name}' but it is not registered. Call RegisterLayer<{layer.Name}>() before Allocate().");
        }
        foreach (var layer in ClearListFor(fromOrder, toOrder, skip))
            map.DataStore.ClearLayer(layer);
        for (int i = 0; i < _orderedCache.Count; i++)
        {
            if (_orderOfCache[i] < fromOrder || _orderOfCache[i] > toOrder) continue;
            if (skip == null || !skip(_orderedCache[i])) _orderedCache[i].Execute(map);
        }
    }

    // Validates an [fromOrder, toOrder] execution range, returning fromOrder.
    private static int CheckedRange(int fromOrder, int toOrder)
    {
        if (toOrder < fromOrder) throw new ArgumentOutOfRangeException(nameof(toOrder), toOrder, $"toOrder {toOrder} is below fromOrder {fromOrder}.");
        return fromOrder;
    }

    // Same rule as the full-run list (a layer is cleared by its first writer that does not read it),
    // evaluated over only the stages that will run: a layer written by a skipped or out-of-range stage
    // and merely rewritten downstream (read-modify-write) must survive.
    private List<Type> ClearListFor(int fromOrder, int toOrder, Predicate<IWorldGeneratorStage>? skip)
    {
        if (fromOrder == int.MinValue && toOrder == int.MaxValue && skip == null) return _clearCache;
        if (skip == null && toOrder == int.MaxValue && _clearFromCache.TryGetValue(fromOrder, out var cached)) return cached;
        var list = new List<Type>();
        var written = new HashSet<Type>();
        for (int i = 0; i < _orderedCache.Count; i++)
        {
            if (_orderOfCache[i] < fromOrder || _orderOfCache[i] > toOrder) continue;
            var stage = _orderedCache[i];
            if (_orderOfCache[i] > toOrder || (skip != null && skip(stage))) continue;
            var stageReads = StageMetadata.GetReads(stage);
            foreach (var w in StageMetadata.GetWrites(stage))
                if (written.Add(w) && Array.IndexOf(stageReads, w) < 0)
                    list.Add(w);
        }
        if (skip == null && toOrder == int.MaxValue) _clearFromCache[fromOrder] = list;
        return list;
    }
}
