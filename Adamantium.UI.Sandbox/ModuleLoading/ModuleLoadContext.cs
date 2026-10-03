using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Adamantium.UI.Sandbox.ModuleLoading;

/// <summary>Where a module assembly is loaded. What the shell ships - the module contract, the UI, the engine - is the
/// shell's own, so a module's <c>EditorModule</c> is the very type the shell knows; only what the module alone brings
/// is loaded from beside it.</summary>
public sealed class ModuleLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> ShellAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(Path.GetFileNameWithoutExtension)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly AssemblyDependencyResolver _resolver;

    public ModuleLoadContext(string path) : base(Path.GetFileNameWithoutExtension(path))
    {
        _resolver = new AssemblyDependencyResolver(path);
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        if (ShellAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path == null ? null : LoadFromAssemblyPath(path);
    }
}
