using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
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
/// The Core contracts assembly itself is shared, as are Core's references and
/// anything already loaded in the default context (runtime, Jint, ...): those
/// requests return null so they fall back to the default context. That keeps a single
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
        if (IsSharedWithHost(assemblyName))
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

    private static readonly string CoreName = typeof(IWorldGeneratorStage).Assembly.GetName().Name!;

    // Core's own references: they appear in its public surface, so a plugin-local
    // copy would give a second, incompatible type identity.
    private static readonly HashSet<string> CoreReferences = new(
        typeof(IWorldGeneratorStage).Assembly.GetReferencedAssemblies().Select(a => a.Name!),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Host wins for the contracts, Core's references, and anything the default
    /// context already loaded (framework, Jint, ...). Plugin-private
    /// dependencies still resolve from the plugin directory.
    /// </summary>
    private static bool IsSharedWithHost(AssemblyName name)
    {
        var simple = name.Name;
        if (simple == null) return false;
        if (simple == CoreName || CoreReferences.Contains(simple)) return true;
        foreach (var loaded in Default.Assemblies)
            if (string.Equals(loaded.GetName().Name, simple, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Cheap metadata-only probe: false for files that are valid images but
    /// cannot hold a stage (native libraries, managed dependencies that do not
    /// reference Core). Files that cannot be parsed return true so the real load
    /// reports the failure instead of hiding a corrupt plugin.
    /// </summary>
    public static bool MayContainStages(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return false;
            var md = pe.GetMetadataReader();
            foreach (var handle in md.AssemblyReferences)
                if (md.GetString(md.GetAssemblyReference(handle).Name) == CoreName)
                    return true;
            return false;
        }
        catch (BadImageFormatException) { return true; }
        catch (IOException) { return true; }
    }

    /// <summary>Load the entry plugin assembly from disk into this context.</summary>
    public Assembly LoadPluginAssembly(string pluginPath) => LoadFromAssemblyPath(Path.GetFullPath(pluginPath));

    /// <summary>Request cooperative unload of this context.</summary>
    public void Reset() => Unload();
}
