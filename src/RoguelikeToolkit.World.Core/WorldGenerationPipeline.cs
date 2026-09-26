using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeToolkit.World.Core;

public class WorldGenerationPipeline : IDisposable
{
    private readonly List<IWorldGeneratorStage> _stages = new();
    // Cached execution order + contract validation. Rebuilt only when the
    // stage set changes, so repeated Execute calls stay allocation-free and
    // keep the zero-GC generation guarantee for hot paths.
    private readonly List<IWorldGeneratorStage> _orderedCache = new();
    // Declared (stage name, written layer) pairs, rebuilt with the order
    // cache so steady-state Execute validates without reflection.
    private readonly List<(string Stage, Type Layer)> _writesCache = new();
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
        _orderedCache.Clear();
        _writesCache.Clear();
        _cacheDirty = true;
    }

    /// <summary>
    /// Discovers and loads IWorldGeneratorStage implementations with WorldGeneratorStageAttribute
    /// from the current assembly and any .dll files in the specified directory.
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
            foreach (var file in dllFiles)
            {
                try
                {
                    assemblies.Add(Assembly.LoadFrom(file));
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new StageDiscoveryDiagnostic($"Failed to load plugin assembly '{file}'.", ex));
                }
            }
        }

        var stageTypes = assemblies
            .SelectMany(a => {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
            })
            .Where(t => typeof(IWorldGeneratorStage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<WorldGeneratorStageAttribute>() })
            .Where(t => t.Attribute != null)
            .ToList();

        var knownStageTypes = _stages.Select(stage => stage.GetType())
            .Concat(stageTypes.Select(stage => stage.Type))
            .Distinct()
            .Select(type => new { Type = type, Attribute = type.GetCustomAttribute<WorldGeneratorStageAttribute>() })
            .Where(type => type.Attribute != null)
            .ToList();

        var duplicateOrders = knownStageTypes
            .GroupBy(t => t.Attribute!.Order)
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
            var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
                .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
            if (attr == null) continue;
            foreach (var t in (attr.Reads ?? Array.Empty<Type>()).Concat(attr.Writes ?? Array.Empty<Type>()).Distinct())
            {
                if (!map.DataStore.IsLayerRegistered(t))
                    throw new InvalidOperationException(
                        $"Stage '{stage.GetType().Name}' declares layer '{t.Name}' but it is not registered. Call RegisterLayer<{t.Name}>() before Allocate().");
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
            _orderedCache.AddRange(_stages.OrderBy(s =>
                s.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
                    .OfType<WorldGeneratorStageAttribute>().FirstOrDefault()?.Order ?? int.MaxValue));
            _writesCache.Clear();
            foreach (var stage in _orderedCache)
            {
                var attr = stage.GetType().GetCustomAttributes(typeof(WorldGeneratorStageAttribute), false)
                    .OfType<WorldGeneratorStageAttribute>().FirstOrDefault();
                if (attr?.Writes == null) continue;
                foreach (var w in attr.Writes)
                    _writesCache.Add((stage.GetType().Name, w));
            }
            _cacheDirty = false;
        }
        // Fail fast on missing written layers (Writes only: ReadsOptional
        // layers are allowed to be absent). Without this the first stage
        // touching the layer throws a bare ArgumentException from GetSpan.
        foreach (var (stageName, layer) in _writesCache)
        {
            if (!map.DataStore.IsLayerRegistered(layer))
                throw new InvalidOperationException(
                    $"Stage '{stageName}' writes layer '{layer.Name}' but it is not registered. Call RegisterLayer<{layer.Name}>() before Allocate().");
        }
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
