using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeToolkit.World.Core;

public class WorldGenerationPipeline
{
    private readonly List<IWorldGeneratorStage> _stages = new();

    public IReadOnlyList<IWorldGeneratorStage> Stages => _stages;

    public void AddStage(IWorldGeneratorStage stage)
    {
        _stages.Add(stage);
    }

    /// <summary>
    /// Discovers and loads IWorldGeneratorStage implementations with WorldGeneratorStageAttribute
    /// from the current assembly and any .dll files in the specified directory.
    /// Throws InvalidOperationException if duplicate Order values are found.
    /// </summary>
    public void Discover(string? pluginDirectory = null)
    {
        _stages.Clear();
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
                    // Log or handle assembly load failure
                    Console.WriteLine($"Failed to load plugin assembly: {file}. Error: {ex.Message}");
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

        var duplicateOrders = stageTypes
            .GroupBy(t => t.Attribute!.Order)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateOrders.Any())
        {
            throw new InvalidOperationException($"Duplicate Order values found for WorldGeneratorStageAttribute: {string.Join(", ", duplicateOrders)}");
        }

        var orderedTypes = stageTypes.OrderBy(t => t.Attribute!.Order).ToList();

        foreach (var typeInfo in orderedTypes)
        {
            try
            {
                var instance = (IWorldGeneratorStage)Activator.CreateInstance(typeInfo.Type)!;
                _stages.Add(instance);
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Failed to instantiate pipeline stage: {typeInfo.Type.Name}. Error: {ex.Message}");
            }
        }
    }

    public void Execute(WorldMap map)
    {
        foreach (var stage in _stages)
        {
            stage.Execute(map);
        }
    }
}
