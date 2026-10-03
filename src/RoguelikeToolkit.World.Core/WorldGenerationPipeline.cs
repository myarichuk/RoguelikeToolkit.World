using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeToolkit.World.Core;

public class WorldGenerationPipeline : IDisposable
{
    private readonly List<IWorldGeneratorStage> _stages = new();
    private readonly List<PluginLoadContext> _pluginContexts = new();
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
    private bool _cacheDirty = true;

    public IReadOnlyList<IWorldGeneratorStage> Stages => _stages;

    public void AddStage(IWorldGeneratorStage stage)
    {
        _stages.Add(stage);
        _cacheDirty = true;
    }

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
        foreach (var ctx in _pluginContexts)
        {
            try { ctx.Unload(); } catch { /* cooperative unload is best-effort */ }
        }
        _pluginContexts.Clear();
        _cacheDirty = true;
    }

    /// <summary>
    /// Discovers and loads IWorldGeneratorStage implementations with
    /// <see cref="WorldGeneratorStageAttribute"/> (or <see cref="IDeclaredStage"/>
    /// for runtime stages) from the current assembly and any .dll files in the
    /// specified directory. Compiled C# plugins are full-trust: only load DLLs
    /// you trust. Each plugin file loads into its own collectible
    /// <see cref="PluginLoadContext"/> (shared Core contracts, plugin-local
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

                try
                {
                    var ctx = new PluginLoadContext(file);
                    assemblies.Add(ctx.LoadPluginAssembly(file));
                    _pluginContexts.Add(ctx);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new StageDiscoveryDiagnostic($"Failed to load plugin assembly '{file}'.", ex));
                }
            }
        }

        var stageTypes = assemblies
            .SelectMany(a => {
                try { return (IEnumerable<Type>)a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
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

    public void DiscoverAndReplace(string? pluginDirectory = null, Action<StageDiscoveryDiagnostic>? onDiagnostic = null)
    {
        ResetStages();
        Discover(pluginDirectory, onDiagnostic);
    }


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

    public void Execute(WorldMap map)
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
            var written = new HashSet<Type>();
            foreach (var stage in _orderedCache)
            {
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
        foreach (var layer in _clearCache)
            map.DataStore.ClearLayer(layer);
        foreach (var stage in _orderedCache)
        {
            stage.Execute(map);
        }
    }
}

public sealed class StageDiscoveryDiagnostic
{
    public string Message { get; }
    public Exception Exception { get; }

    public StageDiscoveryDiagnostic(string message, Exception exception)
    {
        Message = message;
        Exception = exception;
    }
}
