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
        "System.Uri",
        "System.TimeSpan",
        "Adamantium.Core.Collections.TrackingCollection<double>",
    ];

    /// <summary>The type a value of <paramref name="type"/> is written as: T for a Nullable&lt;T&gt;, otherwise the type
    /// itself.</summary>
    public static IResolvedType ValueTypeOf(IResolvedType type)
    {
        // Roslyn names it Nullable, reflection Nullable`1.
        return type.IsGenericType && type.Name is "Nullable" or "Nullable`1" ? type.TypeArguments.FirstOrDefault() ?? type : type;
    }

    /// <summary>True when <paramref name="text"/> is what TypeParser reads as a Nullable&lt;T&gt;'s null: nothing, or
    /// "null" in any case.</summary>
    public static bool IsNullLiteral(string text) =>
        string.IsNullOrEmpty(text) || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when generated code may parse a markup string into <paramref name="type"/>.</summary>
    public static bool CanParse(IResolvedType type)
    {
        type = ValueTypeOf(type);

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
