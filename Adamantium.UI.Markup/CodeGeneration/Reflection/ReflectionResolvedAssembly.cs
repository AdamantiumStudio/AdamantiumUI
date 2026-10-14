using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Markup.CodeGeneration.Reflection;

public class ReflectionResolvedAssembly : IResolvedAssembly
{
    // A loaded assembly's types never change: read and indexed once, then shared by every resolver.
    private static readonly ConditionalWeakTable<Assembly, Contents> SharedContents = new();

    private Contents _contents;
    private bool _ownsContents;

    public ReflectionResolvedAssembly(Assembly assembly)
    {
        _contents = assembly.IsDynamic ? Read(assembly) : SharedContents.GetValue(assembly, ReadOf);
    }

    public string Name => _contents.Name;

    public IReadOnlyList<IResolvedType> Types => _contents.Types;

    public IResolvedType GetTypeByShortName(string shortName) =>
        shortName != null && _contents.ByShortName.TryGetValue(shortName, out var type) ? type : null;

    public IEnumerable<IResolvedType> GetTypesByNamespace(string @namespace) =>
        _contents.Types.Where(t => t.Namespace == @namespace);

    public IResolvedType GetTypeByFullName(string fullName) =>
        fullName != null && _contents.ByFullName.TryGetValue(fullName, out var type) ? type : null;

    /// <summary>Whether a type of this assembly is in <paramref name="namespace"/>.</summary>
    public bool HasNamespace(string @namespace) => @namespace != null && _contents.Namespaces.Contains(@namespace);

    public void AddType(IResolvedType type)
    {
        if (_contents.ByFullName.ContainsKey(type.FullName))
        {
            return;
        }

        if (!_ownsContents)
        {
            _contents = _contents.Copy();
            _ownsContents = true;
        }

        _contents.Add(type);
    }

    private static Contents Read(Assembly assembly) => new(SimpleName(assembly),
        SafeGetTypes(assembly).Concat(ForwardedTypes(assembly))
            .Where(t => !t.IsNested)
            .Select(t => (IResolvedType)ReflectionResolvedType.Of(t)));

    private sealed class Contents
    {
        public readonly string Name;
        public readonly List<IResolvedType> Types = [];
        public readonly Dictionary<string, IResolvedType> ByFullName = new(StringComparer.Ordinal);
        public readonly Dictionary<string, IResolvedType> ByShortName = new(StringComparer.Ordinal);
        public readonly HashSet<string> Namespaces = new(StringComparer.Ordinal);

        public Contents(string name, IEnumerable<IResolvedType> types)
        {
            Name = name;
            foreach (var type in types)
            {
                Add(type);
            }
        }

        public Contents Copy() => new(Name, Types);

        // The first type of a name wins, as a search in order would find it.
        public void Add(IResolvedType type)
        {
            Types.Add(type);
            if (!ByFullName.ContainsKey(type.FullName)) ByFullName.Add(type.FullName, type);
            if (!ByShortName.ContainsKey(type.Name)) ByShortName.Add(type.Name, type);
            Namespaces.Add(type.Namespace);
        }
    }

    private static IEnumerable<Type> ForwardedTypes(Assembly assembly)
    {
        try
        {
            return typeof(Assembly).GetMethod("GetForwardedTypes", Type.EmptyTypes)?.Invoke(assembly, null) as Type[] ?? [];
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ReflectionTypeLoadException loadFailure)
        {
            return loadFailure.Types.Where(t => t != null);
        }
    }

    /// <summary>The types of <paramref name="asm"/> that load, read once per assembly: a loaded assembly's types do not
    /// change, except a dynamic one's, which is read every time.</summary>
    public static IEnumerable<Type> SafeGetTypes(Assembly asm) => asm.IsDynamic ? LoadTypes(asm) : TypesOf.GetValue(asm, LoadTypesOf);

    /// <summary>The first top-level type of <paramref name="asm"/> named <paramref name="shortName"/>, in the order the
    /// assembly lists its types.</summary>
    public static Type TopLevelTypeNamed(Assembly asm, string shortName)
    {
        if (shortName == null)
        {
            return null;
        }

        var index = asm.IsDynamic ? IndexTopLevel(asm) : TopLevelByName.GetValue(asm, IndexTopLevelOf);
        return index.TryGetValue(shortName, out var type) ? type : null;
    }

    /// <summary>Whether a type of <paramref name="asm"/>, nested ones included, is in <paramref name="namespace"/>.</summary>
    public static bool DeclaresNamespace(Assembly asm, string @namespace)
    {
        if (@namespace == null)
        {
            foreach (var type in SafeGetTypes(asm))
                if (type.Namespace == null) return true;
            return false;
        }

        var namespaces = asm.IsDynamic ? NamespacesIn(asm) : NamespacesOf.GetValue(asm, NamespacesInOf);
        return namespaces.Contains(@namespace);
    }

    /// <summary><paramref name="asm"/>'s <see cref="Assembly.GetType(string, bool)"/>, remembered per name.</summary>
    public static Type TypeNamed(Assembly asm, string metadataName)
    {
        if (asm.IsDynamic)
        {
            return asm.GetType(metadataName, throwOnError: false);
        }

        var known = TypesByName.GetValue(asm, NewTypeTableOf);
        if (known.TryGetValue(metadataName, out var type))
        {
            return type;
        }

        return known.GetOrAdd(metadataName, asm.GetType(metadataName, throwOnError: false));
    }

    /// <summary><paramref name="asm"/>'s simple name, without building an <see cref="AssemblyName"/> each time.</summary>
    public static string SimpleName(Assembly asm) => SimpleNames.GetValue(asm, SimpleNameOf);

    private static readonly ConditionalWeakTable<Assembly, ConcurrentDictionary<string, Type>> TypesByName = new();
    private static readonly ConditionalWeakTable<Assembly, string> SimpleNames = new();
    private static readonly ConditionalWeakTable<Assembly, HashSet<string>> NamespacesOf = new();
    private static readonly ConditionalWeakTable<Assembly, Type[]> TypesOf = new();
    private static readonly ConditionalWeakTable<Assembly, Dictionary<string, Type>> TopLevelByName = new();

    // Made once: a method group passed as a callback is a new delegate on every call.
    private static readonly ConditionalWeakTable<Assembly, Type[]>.CreateValueCallback LoadTypesOf = LoadTypes;
    private static readonly ConditionalWeakTable<Assembly, Dictionary<string, Type>>.CreateValueCallback IndexTopLevelOf = IndexTopLevel;
    private static readonly ConditionalWeakTable<Assembly, HashSet<string>>.CreateValueCallback NamespacesInOf = NamespacesIn;
    private static readonly ConditionalWeakTable<Assembly, ConcurrentDictionary<string, Type>>.CreateValueCallback NewTypeTableOf =
        static _ => new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<Assembly, string>.CreateValueCallback SimpleNameOf = static a => a.GetName().Name;
    private static readonly ConditionalWeakTable<Assembly, Contents>.CreateValueCallback ReadOf = Read;

    private static HashSet<string> NamespacesIn(Assembly asm)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in SafeGetTypes(asm))
            if (type.Namespace != null) namespaces.Add(type.Namespace);
        return namespaces;
    }

    private static Type[] LoadTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        catch { return []; }
    }

    private static Dictionary<string, Type> IndexTopLevel(Assembly asm)
    {
        var index = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in SafeGetTypes(asm))
        {
            if (!type.IsNested && !index.ContainsKey(type.Name)) index.Add(type.Name, type);
        }

        return index;
    }
}
