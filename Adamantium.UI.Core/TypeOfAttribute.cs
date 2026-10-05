namespace Adamantium.UI.Core;

/// <summary>Limits a <see cref="Type"/>-valued property to types derived from <see cref="BaseType"/>: markup completion
/// offers only those, and the build rejects any other.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TypeOfAttribute : Attribute
{
    public TypeOfAttribute(Type baseType)
    {
        BaseType = baseType;
    }

    public Type BaseType { get; }
}
