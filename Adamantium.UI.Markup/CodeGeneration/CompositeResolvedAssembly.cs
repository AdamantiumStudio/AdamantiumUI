namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>
/// An <see cref="IResolvedAssembly"/> view over several assemblies that share one xmlns URI (e.g. the controls
/// assembly and the core assembly both mapped to "http://adamantium/ui"). Read-only: it unions their types and
/// forwards look-ups to the first part that resolves. The resolver builds one of these when more than one
/// [XmlnsDefinition] maps to the same URI, so consumers keep using a single IResolvedAssembly transparently.
/// </summary>
public sealed class CompositeResolvedAssembly : IResolvedAssembly
{
    private readonly IReadOnlyList<IResolvedAssembly> _parts;

    public CompositeResolvedAssembly(IReadOnlyList<IResolvedAssembly> parts) => _parts = parts;

    public string Name => string.Join("+", _parts.Select(p => p.Name));

    // The union is made again only when a part has gained a type since.
    public IReadOnlyList<IResolvedType> Types
    {
        get
        {
            var count = 0;
            for (var i = 0; i < _parts.Count; i++)
            {
                count += _parts[i].Types.Count;
            }

            if (_types == null || count != _unionOf)
            {
                _types = Union();
                _unionOf = count;
            }

            return _types;
        }
    }

    private IReadOnlyList<IResolvedType> _types;
    private int _unionOf;

    private List<IResolvedType> Union()
    {
        var seen = new HashSet<string>();
        var list = new List<IResolvedType>();
        foreach (var part in _parts)
        foreach (var type in part.Types)
            if (seen.Add(type.FullName ?? type.Name)) list.Add(type);   // first part wins on a name clash
        return list;
    }

    public IResolvedType GetTypeByShortName(string shortName)
    {
        for (var i = 0; i < _parts.Count; i++)
        {
            var type = _parts[i].GetTypeByShortName(shortName);
            if (type != null) return type;
        }
        return null;
    }

    public IEnumerable<IResolvedType> GetTypesByNamespace(string @namespace) =>
        _parts.SelectMany(p => p.GetTypesByNamespace(@namespace));

    public IResolvedType GetTypeByFullName(string fullName)
    {
        for (var i = 0; i < _parts.Count; i++)
        {
            var type = _parts[i].GetTypeByFullName(fullName);
            if (type != null) return type;
        }
        return null;
    }

    public void AddType(IResolvedType type) =>
        throw new NotSupportedException("A composite xmlns assembly is read-only.");
}
