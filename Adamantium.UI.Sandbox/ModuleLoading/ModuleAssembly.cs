using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.Modules;

namespace Adamantium.UI.Sandbox.ModuleLoading;

/// <summary>Loads a module assembly beside the shell.</summary>
public static class ModuleAssembly
{
    /// <summary>Every class in the assembly derived from <see cref="EditorModule"/> becomes a module, installed; every
    /// resource dictionary joins the application's resources, where the ribbon finds the module's tabs by key. An
    /// assembly already loaded under the same name is the one read again, never a second copy.</summary>
    public static LoadedModules Load(string path)
    {
        Type[] types;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var name = AssemblyName.GetAssemblyName(fullPath).Name;
            var assembly = AssemblyLoadContext.All
                               .SelectMany(context => context.Assemblies)
                               .FirstOrDefault(loaded => loaded.GetName().Name == name)
                           ?? new ModuleLoadContext(fullPath).LoadFromAssemblyPath(fullPath);
            types = assembly.GetTypes();
        }
        catch (Exception e) when (e is BadImageFormatException or FileLoadException or FileNotFoundException
                                       or ReflectionTypeLoadException)
        {
            return new LoadedModules { Failed = true };
        }

        if (UIAppContext.Current?.ResourceManager is { } resources)
        {
            var owner = new ModuleResources { AssemblyName = Path.GetFileNameWithoutExtension(path) };
            foreach (var dictionary in types.Where(type => !type.IsAbstract && typeof(ResourceDictionary).IsAssignableFrom(type)))
            {
                resources.AddSource(owner, dictionary, ResourceScope.Global);
            }

            resources.NotifyResourcesChanged();
        }

        var modules = types
            .Where(type => !type.IsAbstract && typeof(EditorModule).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => (EditorModule)Activator.CreateInstance(type))
            .ToList();

        return new LoadedModules { Modules = modules };
    }
}
