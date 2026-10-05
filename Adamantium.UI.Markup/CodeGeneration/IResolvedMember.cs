namespace Adamantium.UI.Markup.CodeGeneration;

public interface IResolvedMember
{
    string Name { get; }
    
    IResolvedType MemberType { get; }
    
    IResolvedType DeclaringType { get; }
    
    bool HasAttribute(string attributeMetadataName);

    IResolvedAttribute GetAttribute(string attributeMetadataName);

    bool HasSetter();

    bool IsStatic { get; }

    bool IsPublic { get; }

    /// <summary>A method's parameters by name, in order; empty for any other member.</summary>
    IReadOnlyList<string> ParameterNames { get; }

    ResolvedMemberKind MemberKind { get; }
}