using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.Markup.Building;

/// <summary>A type, named as the generated code names it: the owner of a static member, or what is created.</summary>
public sealed class TypeValue(string qualifiedName, IResolvedType resolved = null) : BuildValue
{
    /// <summary>The name with its <c>global::</c> prefix, or a C# keyword such as <c>double</c>.</summary>
    public string QualifiedName { get; } = qualifiedName;

    /// <summary>The type as the resolver knows it, when the plan was made from one; null for a type named by text.</summary>
    public IResolvedType Resolved { get; } = resolved;
}
