using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Generators.Roslyn;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.Localization;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators;

/// <summary>Writes what a project's application blueprint makes of it: the attribute that names the blueprint to the
/// assembly and the entry point that runs the application - after checking there is one blueprint, one application
/// class and no entry point written by hand.</summary>
public static class ApplicationEntryPoint
{
    private const string ApplicationBaseName = "Adamantium.UI.UIApplication";

    /// <summary>Checks the project's blueprints and, when they make one application, writes its entry point.</summary>
    public static void Emit(SourceProductionContext context, Compilation compilation, IReadOnlyList<(string File, string ClassName)> blueprints,
        AumlSourceGenerator codeGenerator, string rootNamespace)
    {
        if (blueprints.Count == 0)
        {
            return;
        }

        if (blueprints.Count > 1)
        {
            Report(context, "AUI020", ApplicationBlueprintRules.TooMany(blueprints.Select(b => b.File).ToList()));
            return;
        }

        var blueprint = blueprints[0];
        var handWritten = SourceTypes(compilation.Assembly.GlobalNamespace)
            .SelectMany(type => type.GetMembers("Main").OfType<IMethodSymbol>())
            .FirstOrDefault(method => method.IsStatic);
        if (handWritten != null)
        {
            Report(context, "AUI021",
                MarkupMessages.EntryPointHandWritten(blueprint.File, handWritten.ContainingType.ToDisplayString()));
            return;
        }

        var applications = SourceTypes(compilation.Assembly.GlobalNamespace).Where(IsApplication).ToList();
        if (applications.Count != 1)
        {
            Report(context, "AUI022", applications.Count == 0
                ? MarkupMessages.BlueprintNoApplication(blueprint.File)
                : MarkupMessages.BlueprintManyApplications(blueprint.File, applications.Count,
                    string.Join(", ", applications.Select(a => a.ToDisplayString()))));
            return;
        }

        codeGenerator.GenerateApplicationEntry(new RoslynOutputSink(context), blueprint.ClassName,
            applications[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), rootNamespace);
    }

    private static bool IsApplication(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic
            || !type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
        {
            return false;
        }

        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == ApplicationBaseName)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in SourceTypes(child))
            {
                yield return type;
            }
        }
    }

    private static void Report(SourceProductionContext context, string id, string message) =>
        context.ReportDiagnostic(Diagnostic.Create(id, "Build", message, DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 0));
}
