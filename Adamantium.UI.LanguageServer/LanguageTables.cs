using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.LanguageServer;

/// <summary>Finds the language tables of a compilation: its own and those of the assemblies it references.</summary>
public static class LanguageTables
{
    private const string TableBase = "Adamantium.UI.Core.Localization.LocalizedStrings";

    public static IReadOnlyList<LanguageTableInfo> Collect(Compilation compilation)
    {
        var result = new List<LanguageTableInfo>();
        Collect(compilation.Assembly.GlobalNamespace, result);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var name = assembly.Identity.Name;
            if (name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                name is "mscorlib" or "netstandard")
            {
                continue;
            }

            Collect(assembly.GlobalNamespace, result);
        }

        return result;
    }

    private static void Collect(INamespaceSymbol @namespace, List<LanguageTableInfo> result)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            if (type.DeclaredAccessibility == Accessibility.Public && !type.IsAbstract && DerivesFromTable(type))
            {
                result.Add(Describe(type));
            }
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            Collect(child, result);
        }
    }

    private static LanguageTableInfo Describe(INamedTypeSymbol type)
    {
        var strings = new List<LanguageStringInfo>();
        foreach (var member in type.GetMembers().Where(m => m.DeclaredAccessibility == Accessibility.Public && m.IsStatic))
        {
            if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_String, Parameters.Length: 0 } property)
            {
                strings.Add(new LanguageStringInfo(property.Name, [], Text(property)));
            }
            else if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnType.SpecialType: SpecialType.System_String } method)
            {
                strings.Add(new LanguageStringInfo(method.Name, method.Parameters.Select(p => p.Name).ToList(), Text(method)));
            }
        }

        return new LanguageTableInfo(type.Name, type.ToDisplayString(), strings);
    }

    // The generator writes each string's base text as the member's summary.
    private static string Text(ISymbol member)
    {
        var xml = member.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml))
        {
            return null;
        }

        try
        {
            return XElement.Parse(xml).Element("summary")?.Value.Trim();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool DerivesFromTable(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == TableBase)
            {
                return true;
            }
        }

        return false;
    }
}
