using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>Reads the framework attributes that say what a markup value may name: <c>[TypeOf]</c> on a
/// <see cref="System.Type"/>-valued property and <c>[MarkupFile]</c> on a type written as a file path.</summary>
public static class MarkupValueAttributes
{
    public const string TypeOfAttributeName = "Adamantium.UI.Core.TypeOfAttribute";

    public const string MarkupFileAttributeName = "Adamantium.UI.Core.MarkupFileAttribute";

    /// <summary>The full name of the type the member's value must derive from; null when any type fits. Read from the
    /// constructor's argument in a compilation and from the attribute's property at run time.</summary>
    public static string TypeOfBase(this IResolvedMember member)
    {
        var attribute = member?.GetAttribute(TypeOfAttributeName);
        if (attribute == null)
        {
            return null;
        }

        if (attribute.ConstructorArguments.Count > 0)
        {
            return attribute.ConstructorArguments[0]?.ToString();
        }

        return attribute.NamedArguments.TryGetValue("BaseType", out var baseType) ? baseType?.ToString() : null;
    }

    /// <summary>Why <paramref name="type"/> cannot be the member's value - it does not derive from the member's
    /// <c>[TypeOf]</c> base; null when it can.</summary>
    public static string TypeOfProblem(this IResolvedMember member, IResolvedType type)
    {
        var baseName = member.TypeOfBase();
        if (baseName == null || type == null || type.IsAssignableTo(baseName))
        {
            return null;
        }

        var baseShortName = baseName.Substring(baseName.LastIndexOf('.') + 1);
        return MarkupMessages.TypeNotDerived(type.Name, baseShortName, $"{member.DeclaringType?.Name}.{member.Name}", baseName);
    }

    /// <summary>The extensions of the files the member's value is written as, declared on the member or on its type; null
    /// when its value is not a file.</summary>
    public static IReadOnlyList<string> FileExtensions(this IResolvedMember member) =>
        member == null ? null : Extensions(member.GetAttribute(MarkupFileAttributeName)) ?? member.MemberType.FileExtensions();

    /// <summary>The extensions of the files a value of this type is written as, declared on it or a base; null when its
    /// value is not a file.</summary>
    public static IReadOnlyList<string> FileExtensions(this IResolvedType type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (Extensions(current.GetAttribute(MarkupFileAttributeName)) is { } extensions)
            {
                return extensions;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> Extensions(IResolvedAttribute attribute) =>
        attribute?.ConstructorArguments.FirstOrDefault() is object[] extensions ? extensions.Select(e => e.ToString()).ToList() : null;
}
