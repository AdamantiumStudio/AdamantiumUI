using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Markup.CodeGeneration.Roslyn;

public class RoslynResolvedAssembly : IResolvedAssembly
{
    private readonly IAssemblySymbol _assemblySymbol;
    private readonly List<IResolvedType> _types;
    private Dictionary<string, IResolvedType> _byFullName;
    private Dictionary<string, IResolvedType> _byShortName;

    public RoslynResolvedAssembly(IAssemblySymbol assemblySymbol)
    {
        _assemblySymbol = assemblySymbol;
        Name = _assemblySymbol.Name;

        var currentNamespace = assemblySymbol.GlobalNamespace.GetNamespaceMembers().FirstOrDefault();

        _types = new List<IResolvedType>();

        var nsStack = new Stack<INamespaceSymbol>();
        nsStack.Push(_assemblySymbol.GlobalNamespace);
        while (nsStack.Count > 0)
        {
            var nsSymbol = nsStack.Pop();
            var members = nsSymbol.GetTypeMembers().Select(x=>new RoslynResolvedType(x)).ToList();
            _types.AddRange(members);

            foreach (var nsMember in nsSymbol.GetNamespaceMembers())
            {
                nsStack.Push(nsMember);
            }
        }
    }

    public string Name { get; }

    // All available types in assembly
    public IReadOnlyList<IResolvedType> Types => _types;
    public IResolvedType GetTypeByShortName(string shortName)
    {
        if (shortName == null)
        {
            return null;
        }

        EnsureIndexed();
        return _byShortName.TryGetValue(shortName, out var type) ? type : null;
    }

    public IEnumerable<IResolvedType> GetTypesByNamespace(string @namespace)
    {
        return _types.Where(t => t.Namespace == @namespace);
    }

    public IResolvedType GetTypeByFullName(string fullName)
    {
        if (fullName == null)
        {
            return null;
        }

        EnsureIndexed();
        return _byFullName.TryGetValue(fullName, out var type) ? type : null;
    }

    public void AddType(IResolvedType type)
    {
        EnsureIndexed();

        // this type already exists in this assembly
        if (_byFullName.ContainsKey(type.FullName))
            return;

        _types.Add(type);
        Index(type);
    }

    private void EnsureIndexed()
    {
        if (_byFullName != null)
        {
            return;
        }

        _byFullName = new Dictionary<string, IResolvedType>();
        _byShortName = new Dictionary<string, IResolvedType>();
        foreach (var type in _types)
        {
            Index(type);
        }
    }

    private void Index(IResolvedType type)
    {
        if (!_byFullName.ContainsKey(type.FullName))
        {
            _byFullName[type.FullName] = type;
        }

        if (!_byShortName.ContainsKey(type.Name))
        {
            _byShortName[type.Name] = type;
        }
    }

    public override string ToString()
    {
        return Name;
    }
}