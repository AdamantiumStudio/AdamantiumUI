namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>
/// Whether the run-time TypeParser can turn a markup string into a type, decided at build time the way it decides at run
/// time: a parser the framework registers (the types ParserRegistry starts with, listed here as the analyzer cannot load
/// it), a [TypeParser] attribute on the type or a base, an enum, an IConvertible, or the type's own public static
/// Parse(string). Generated code for any other type would throw where the view is built.
/// </summary>
public static class TypeParserCheck
{
    private const string TypeParserAttribute = "Adamantium.Core.TypeParsing.TypeParserAttribute";

    private static readonly HashSet<string> Registered =
    [
        "Adamantium.Mathematics.Color",
        "Adamantium.Mathematics.Thickness",
        "Adamantium.Mathematics.Vector2",
        "System.Uri",
        "System.TimeSpan",
        "Adamantium.Core.Collections.TrackingCollection<double>",
    ];

    /// <summary>True when generated code may parse a markup string into <paramref name="type"/>.</summary>
    public static bool CanParse(IResolvedType type)
    {
        if (type.IsGenericType && type.Name == "Nullable")
        {
            type = type.TypeArguments.FirstOrDefault() ?? type;
        }

        if (Registered.Contains(type.FullName) || type.TypeKind == ResolvedTypeKind.Enum
            || type.ImplementsInterface("IConvertible"))
        {
            return true;
        }

        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.HasAttribute(TypeParserAttribute))
            {
                return true;
            }
        }

        return type.Members.Any(m => m is { Name: "Parse", MemberKind: ResolvedMemberKind.Method, IsStatic: true, IsPublic: true }
                                     && m.ParameterNames.Count == 1
                                     && m.MemberType?.FullName == type.FullName);
    }
}
