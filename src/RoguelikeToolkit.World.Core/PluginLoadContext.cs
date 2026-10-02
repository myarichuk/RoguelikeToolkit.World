using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Isolated, collectible load context for one compiled C# plugin assembly.
/// Full-trust code runs here (same powers as any referenced DLL), so only
/// load plugins you trust. Isolation gives two things over bare
/// <c>Assembly.LoadFrom</c>: plugin dependencies resolve from the plugin's own
/// directory first (fewer version collisions), and <see cref="Reset"/> /
/// pipeline <c>ResetStages</c> can unload the plugin when nothing references it.
/// </summary>
/// <remarks>
/// The Core contracts assembly itself is shared: requests for
/// <c>RoguelikeToolkit.World.Core</c> (and the runtime / SharpArena
/// dependencies already loaded in the default context) return null so they
/// fall back to the default context. That keeps a single
/// <see cref="IWorldGeneratorStage"/> type identity across host and plugins.
/// Unload is cooperative: it completes once no threads run plugin code and no
/// strong references to plugin types/instances escape (pipeline drops its stage
/// references on reset).
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base(name: $"WorldPlugin:{Path.GetFileName(pluginPath)}", isCollectible: true)
    {
        _pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath)) ?? AppContext.BaseDirectory;
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share the contracts + already-loaded framework deps with the host.
        // Returning null falls back to the default context.
        if (assemblyName.Name == typeof(IWorldGeneratorStage).Assembly.GetName().Name)
            return null;

        string? candidate = _resolver.ResolveAssemblyToPath(assemblyName);
        if (candidate != null && File.Exists(candidate))
            return LoadFromAssemblyPath(candidate);

        // Probe the plugin directory by simple name as a fallback (unmanaged
        // resolver covers the .deps.json case above; this covers bare drops).
        string probe = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
        if (File.Exists(probe))
            return LoadFromAssemblyPath(probe);

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? candidate = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (candidate != null && File.Exists(candidate))
            return LoadUnmanagedDllFromPath(candidate);
        return IntPtr.Zero;
    }

    /// <summary>Load the entry plugin assembly from disk into this context.</summary>
    public Assembly LoadPluginAssembly(string pluginPath) => LoadFromAssemblyPath(Path.GetFullPath(pluginPath));

    /// <summary>Request cooperative unload of this context.</summary>
    public void Reset() => Unload();
}
