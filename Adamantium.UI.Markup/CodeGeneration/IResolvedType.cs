namespace Adamantium.UI.Markup.CodeGeneration;

public interface IResolvedType
{
    string Name { get; }
    string Namespace { get; }
    string FullName { get; }

    /// <summary>The name generated code writes: global::-qualified, so a namespace of the project's own (one ending in
    /// Adamantium, say) cannot capture it.</summary>
    string QualifiedName { get; }

    string AssemblyName { get; }
    
    bool IsNamedType { get; }
    
    bool IsGenericType { get; }

    /// <summary>Whether markup can build it as an element: neither abstract, static nor an interface, and with a public
    /// constructor that takes nothing.</summary>
    bool IsCreatable { get; }
    bool InheritsFrom(string baseTypeName);
    
    bool HasAttribute(string attributeName);
    
    IResolvedAttribute GetAttribute(string fullName);
    IEnumerable<IResolvedAttribute> GetAttributes();
    
    EntityType EntityType { get; }
    
    IEnumerable<IResolvedType> TypeArguments { get; }

    IResolvedType BaseType { get; }

    IEnumerable<IResolvedMember> Members { get; }
    
    IResolvedMember GetMemberByName(string memberName);

    List<IResolvedProperty> GetAllProperties();

    bool IsAssignableTo(string fullName);

    bool ImplementsInterface(string interfaceName);

    bool IsCollection();

    IResolvedType GetInterface(string interfaceName);

    bool InheritsFromMarkupExtension(string fullyQualifiedName);

    bool FindPropertyWithAttribute(string attributeFullName, out IResolvedProperty property);
    
    ResolvedSpecialType SpecialType { get; }

    ResolvedTypeKind TypeKind { get; }
    
    ResolvedMemberKind MemberKind { get; }
}