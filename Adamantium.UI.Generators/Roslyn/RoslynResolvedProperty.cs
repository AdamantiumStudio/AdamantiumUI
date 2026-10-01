using System.Linq;
using Adamantium.UI.Markup.CodeGeneration;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators.Roslyn;

public class RoslynResolvedProperty : IResolvedProperty
{
    private readonly IPropertySymbol _symbol;

    public RoslynResolvedProperty(IPropertySymbol symbol)
    {
        _symbol = symbol;
    }

    public string Name => _symbol.Name;

    public IResolvedType PropertyType => new RoslynResolvedType(_symbol.Type);

    public bool HasAttribute(string attributeFullName)
    {
        return _symbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.ToDisplayString() == attributeFullName);
    }
}