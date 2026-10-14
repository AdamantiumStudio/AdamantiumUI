using System.Reflection;

namespace Adamantium.UI.Markup.CodeGeneration.Reflection;

public class ReflectionResolvedProperty : IResolvedProperty
{
    private readonly PropertyInfo _property;
    private object[] _attributes;

    public ReflectionResolvedProperty(PropertyInfo property) => _property = property;

    public string Name => _property.Name;

    public IResolvedType PropertyType => ReflectionResolvedType.Of(_property.PropertyType);

    public bool HasAttribute(string attributeFullName)
    {
        foreach (var attribute in _attributes ??= _property.GetCustomAttributes(false))
        {
            if (attribute.GetType().FullName == attributeFullName) return true;
        }

        return false;
    }
}
