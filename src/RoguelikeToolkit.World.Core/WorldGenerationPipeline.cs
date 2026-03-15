using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeToolkit.World.Core;

public class WorldGenerationPipeline : IDisposable
{
    private readonly List<IWorldGeneratorStage> _stages = new();

    public IReadOnlyList<IWorldGeneratorStage> Stages => _stages;

    public void AddStage(IWorldGeneratorStage stage)
    {
        _stages.Add(stage);
    }

    public void ResetStages()
    {
        foreach (var stage in _stages.OfType<IDisposable>())
        {
            stage.Dispose();
        }

        _stages.Clear();
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
    public void Execute(WorldMap map)
    {
        foreach (var stage in _stages)
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
