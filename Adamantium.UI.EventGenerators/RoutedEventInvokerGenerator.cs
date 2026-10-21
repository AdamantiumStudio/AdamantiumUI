using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.UI.EventGenerators;

/// <summary>Writes the direct call of every routed-event handler delegate an assembly declares - a delegate that takes a
/// sender and <c>RoutedEventArgs</c> or a class of them - and of every <c>EventHandler</c> of such args it names with
/// <c>typeof</c>, and registers them with <c>RoutedEventInvokers</c> when the assembly loads.</summary>
[Generator]
public sealed class RoutedEventInvokerGenerator : IIncrementalGenerator
{
    private const string ArgsName = "Adamantium.UI.Core.RoutedEvents.RoutedEventArgs";
    private const string InvokersName = "Adamantium.UI.Core.RoutedEvents.RoutedEventInvokers";

    private static readonly SymbolDisplayFormat PlainFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declared = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is DelegateDeclarationSyntax,
                static (ctx, ct) => Describe(ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) as INamedTypeSymbol,
                    ctx.SemanticModel.Compilation))
            .Where(static call => call is not null);
        var named = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is TypeOfExpressionSyntax typeOf &&
                    typeOf.Type.DescendantNodesAndSelf().OfType<GenericNameSyntax>().Any(),
                static (ctx, ct) => Describe(
                    ctx.SemanticModel.GetTypeInfo(((TypeOfExpressionSyntax)ctx.Node).Type, ct).Type as INamedTypeSymbol,
                    ctx.SemanticModel.Compilation))
            .Where(static call => call is not null);
        var available = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(InvokersName) is not null);

        context.RegisterSourceOutput(declared.Collect().Combine(named.Collect()).Combine(available),
            static (output, source) =>
            {
                if (source.Right)
                {
                    Emit(output, source.Left.Left.AddRange(source.Left.Right));
                }
            });
    }

    private static HandlerCallInfo Describe(INamedTypeSymbol type, Compilation compilation)
    {
        if (type is not { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { ReturnsVoid: true } invoke } ||
            invoke.Parameters.Length != 2 || type.IsUnboundGenericType || !IsReachable(type) ||
            compilation.GetTypeByMetadataName(ArgsName) is not { } argsBase)
        {
            return null;
        }

        var sender = invoke.Parameters[0];
        var args = invoke.Parameters[1];
        var own = compilation.Assembly;
        if (sender.RefKind != RefKind.None || args.RefKind != RefKind.None || sender.Type.IsValueType ||
            !DerivesFrom(args.Type, argsBase) ||
            !CanBeNamed(type, own) || !CanBeNamed(sender.Type, own) || !CanBeNamed(args.Type, own))
        {
            return null;
        }

        return new HandlerCallInfo(type.ToDisplayString(PlainFormat), sender.Type.ToDisplayString(PlainFormat),
            args.Type.ToDisplayString(PlainFormat));
    }

    private static bool CanBeNamed(ITypeSymbol type, IAssemblySymbol own) => type switch
    {
        { TypeKind: TypeKind.Error or TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter } => false,
        IArrayTypeSymbol array => CanBeNamed(array.ElementType, own),
        INamedTypeSymbol named => !RefusesUse(named, own) && named.TypeArguments.All(argument => CanBeNamed(argument, own)) &&
                                  (named.ContainingType is null || CanBeNamed(named.ContainingType, own)),
        _ => true,
    };

    private static bool RefusesUse(ISymbol symbol, IAssemblySymbol own) =>
        symbol.GetAttributes().Any(RefusesUse) ||
        symbol.ContainingAssembly is { } assembly && !SymbolEqualityComparer.Default.Equals(assembly, own) &&
        (assembly.GetAttributes().Any(RefusesUse) || assembly.Modules.Any(m => m.GetAttributes().Any(RefusesUse)));

    private static bool RefusesUse(AttributeData attribute) => attribute.AttributeClass switch
    {
        { Name: "ObsoleteAttribute" } obsolete when obsolete.ContainingNamespace.ToDisplayString() == "System" =>
            attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[1].Value is true ||
            attribute.NamedArguments.Any(a => a.Key == "DiagnosticId"),
        { Name: "ExperimentalAttribute" } experimental =>
            experimental.ContainingNamespace.ToDisplayString() == "System.Diagnostics.CodeAnalysis",
        _ => false,
    };

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol argsBase)
    {
        for (var at = type; at is not null; at = at.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(at, argsBase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReachable(INamedTypeSymbol type)
    {
        for (var at = type; at is not null; at = at.ContainingType)
        {
            if (at.IsFileLocal ||
                at.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return type.TypeArguments.OfType<INamedTypeSymbol>().All(IsReachable);
    }

    private static void Emit(SourceProductionContext output, ImmutableArray<HandlerCallInfo> calls)
    {
        if (calls.IsEmpty)
        {
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#pragma warning disable CA2255, CS0612, CS0618");
        source.AppendLine("namespace Adamantium.UI.Generated;");
        source.AppendLine();
        source.AppendLine("internal static class RoutedEventHandlerCalls");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void Register()");
        source.AppendLine("    {");
        HashSet<string> seen = [];
        foreach (var call in calls)
        {
            if (!seen.Add(call.Delegate))
            {
                continue;
            }

            source.AppendLine($"        global::{InvokersName}.Register(typeof({call.Delegate}),");
            source.AppendLine($"            static (h, s, e) => (({call.Delegate})h)(({call.Sender})s, ({call.Args})e));");
        }

        source.AppendLine("    }");
        source.AppendLine("}");
        output.AddSource("Adamantium.UI.RoutedEventHandlerCalls.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }
}
