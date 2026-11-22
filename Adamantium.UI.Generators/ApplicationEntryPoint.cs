using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Generators.Roslyn;
using Adamantium.UI.Markup.AST;
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
    public static void Emit(SourceProductionContext context, Compilation compilation,
        IReadOnlyList<(string File, string ClassName, EarlyBlueprintValues Early)> blueprints,
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

        codeGenerator.GenerateApplicationEntry(new RoslynOutputSink(context), blueprint.ClassName, blueprint.Early,
            applications[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), rootNamespace);
    }

    /// <summary>What the start reads of a blueprint before making it: its splash and startup windows, its limit on
    /// copies, whether the graphics device debugs, how windows present.</summary>
    public static EarlyBlueprintValues EarlyValuesOf(AumlMetadataContainer blueprint) => new()
    {
        SplashWindow = WindowOf(blueprint, "SplashWindow"),
        StartupWindow = WindowOf(blueprint, "StartupWindow"),
        MaxInstances = int.TryParse(PropertyOf(blueprint, "MaxInstances")?.GetTextValue(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var count) ? count : 0,
        EnableGraphicsDebug = bool.TryParse(PropertyOf(blueprint, "EnableGraphicsDebug")?.GetTextValue(), out var debug) && debug,
        PresentPolicy = PresentPolicyOf(blueprint),
    };

    // Only a plain name of a value goes into generated code; the blueprint's own markup is checked for being one.
    private static string PresentPolicyOf(AumlMetadataContainer blueprint)
    {
        var text = PropertyOf(blueprint, "PresentPolicy")?.GetTextValue()?.Trim();
        return !string.IsNullOrEmpty(text) && text.All(char.IsLetter) ? text : null;
    }

    private static string WindowOf(AumlMetadataContainer blueprint, string name)
    {
        var property = PropertyOf(blueprint, name);
        return property == null ? null : blueprint.TypeResolver.Resolve(property.GetFullTypeValue())?.QualifiedName;
    }

    private static AumlAstPropertyNode PropertyOf(AumlMetadataContainer blueprint, string name) =>
        (blueprint.RootNode as AumlAstObjectNode)?.Children.OfType<AumlAstPropertyNode>()
            .FirstOrDefault(p => p.Property is AumlAstPropertyReference { IsAttachedProperty: false } reference && reference.Name == name);

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
