namespace Adamantium.UI.Core.Resources;

/// <summary>A key a resource dictionary, a theme variant or an application blueprint declares, and the type of what it
/// holds. Written by the build, never by hand: it is how tools find every key an assembly offers - the code that
/// declares them is not readable from outside.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ResourceKeyAttribute : Attribute
{
    public ResourceKeyAttribute(string key, Type valueType)
    {
        Key = key;
        ValueType = valueType;
    }

    public string Key { get; }

    public Type ValueType { get; }
}
