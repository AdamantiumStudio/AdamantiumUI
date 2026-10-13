using System.Reflection;

namespace Adamantium.UI.Markup.CodeGeneration.Reflection;

public class ReflectionResolvedAssembly : IResolvedAssembly
{
    private readonly List<IResolvedType> _types;

    public ReflectionResolvedAssembly(Assembly assembly)
    {
        Name = assembly.GetName().Name;
        _types = SafeGetTypes(assembly).Concat(ForwardedTypes(assembly))
            .Where(t => !t.IsNested)
            .Select(t => (IResolvedType)new ReflectionResolvedType(t))
            .ToList();
    }

    public string Name { get; }

    public IReadOnlyList<IResolvedType> Types => _types;

    public IResolvedType GetTypeByShortName(string shortName) => _types.FirstOrDefault(x => x.Name == shortName);

    public IEnumerable<IResolvedType> GetTypesByNamespace(string @namespace) =>
        _types.Where(t => t.Namespace == @namespace);

    public IResolvedType GetTypeByFullName(string fullName) => _types.FirstOrDefault(x => x.FullName == fullName);

    public void AddType(IResolvedType type)
    {
        if (_types.All(x => x.FullName != type.FullName)) _types.Add(type);
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

    public static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        catch { return Array.Empty<Type>(); }
    }
}
