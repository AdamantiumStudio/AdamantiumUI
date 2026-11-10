using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Adamantium.UI.Generators.WebHost;

/// <summary>Makes, for every interface marked <c>[WebHostObject]</c>, the object a page calls it through and an
/// <c>AddHostObject</c> that adds it to a WebView's bridge.</summary>
[Generator]
public sealed class WebHostObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaces = context.SyntaxProvider.ForAttributeWithMetadataName(WebHostObjectEmitter.AttributeName,
            (node, _) => node is InterfaceDeclarationSyntax,
            (attributed, _) => WebHostObjectEmitter.Emit((INamedTypeSymbol)attributed.TargetSymbol));

        context.RegisterSourceOutput(interfaces, (output, made) =>
        {
            foreach (var diagnostic in made.Diagnostics)
            {
                output.ReportDiagnostic(diagnostic);
            }

            foreach (var (hintName, source) in made.Files)
            {
                output.AddSource(hintName, source);
            }
        });
    }
}
