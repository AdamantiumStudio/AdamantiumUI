using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.Generators.WebHost;

/// <summary>
/// Writes the object a page calls an interface marked <c>[WebHostObject]</c> through - each method under its name in
/// camel case, its arguments read and its answer written as JSON, an awaited method awaited, an answer that is another
/// such interface given as an object of its own - and the <c>AddHostObject</c> that adds one to a bridge. What a page
/// cannot call is an error of the build, not a method that quietly is not there.
/// </summary>
internal static class WebHostObjectEmitter
{
    public const string AttributeName = "Adamantium.UI.WebView.WebHostObjectAttribute";

    private const string Category = "WebView";
    private const string WebView = "global::Adamantium.UI.WebView.";

    private static readonly DiagnosticDescriptor NotAMethod = new("AWV001", "A page calls methods alone",
        "'{0}' is not a method: a page calls an object's methods alone", Category, DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor SameName = new("AWV002", "A page names one method by one name",
        "'{0}' is a second method a page would call '{1}': a name is one method", Category, DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor CannotPass = new("AWV003", "A page passes values",
        "'{0}' takes '{1}' {2}: a page passes values as JSON, or a function - not by reference, nor a cancellation token",
        Category, DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor Generic = new("AWV004", "A page names no type arguments",
        "'{0}' is generic: a page cannot name its type arguments", Category, DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor Nested = new("AWV005", "An object's interface is a namespace's",
        "'{0}' is declared in a type: an interface a page calls is declared in a namespace", Category,
        DiagnosticSeverity.Error, true);

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    public static WebHostObjectSource Emit(INamedTypeSymbol contract)
    {
        var diagnostics = new List<Diagnostic>();
        if (contract.ContainingType != null)
        {
            diagnostics.Add(Diagnostic.Create(Nested, contract.Locations.FirstOrDefault(), contract.Name));
            return new WebHostObjectSource([], diagnostics);
        }

        var methods = new List<IMethodSymbol>();
        var names = new HashSet<string>();
        foreach (var member in new[] { contract }.Concat(contract.AllInterfaces).SelectMany(type => type.GetMembers()))
        {
            if (member.IsStatic)
            {
                continue;
            }

            if (member is IMethodSymbol method)
            {
                if (method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (method.IsGenericMethod)
                {
                    diagnostics.Add(Diagnostic.Create(Generic, Where(method, contract), method.Name));
                    continue;
                }

                foreach (var parameter in method.Parameters)
                {
                    if (parameter.RefKind is RefKind.Ref or RefKind.Out ||
                        parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
                    {
                        diagnostics.Add(Diagnostic.Create(CannotPass, Where(method, contract), method.Name,
                            parameter.Name, parameter.RefKind == RefKind.None ? "as a token" : "by reference"));
                    }
                }

                if (!names.Add(JavaScriptName(method.Name)))
                {
                    diagnostics.Add(Diagnostic.Create(SameName, Where(method, contract), method.Name,
                        JavaScriptName(method.Name)));
                    continue;
                }

                methods.Add(method);
            }
            else if (member is IPropertySymbol or IEventSymbol)
            {
                diagnostics.Add(Diagnostic.Create(NotAMethod, Where(member, contract), member.Name));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new WebHostObjectSource([], diagnostics);
        }

        var space = contract.ContainingNamespace.IsGlobalNamespace ? null : contract.ContainingNamespace.ToDisplayString();
        var access = contract.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var name = HostObjectName(contract);
        var type = contract.ToDisplayString(Full);
        return new WebHostObjectSource(
        [
            (Hint(space, name), HostObject(space, access, name, type, methods)),
            (Hint(space, name + "Bridge"), Bridge(space, access, name, type))
        ], diagnostics);
    }

    private static string HostObject(string space, string access, string name, string type, List<IMethodSymbol> methods)
    {
        var source = Header(space);
        source.Append($$"""
            /// <summary>What a page calls <see cref="{{type}}"/> through.</summary>
            [global::System.CodeDom.Compiler.GeneratedCode("Adamantium.UI.Generators", "1.0")]
            {{access}} sealed class {{name}} : {{WebView}}WebHostObject
            {
                private readonly {{type}} target;

                /// <summary>What a page calls <paramref name="target"/> through.</summary>
                public {{name}}({{type}} target)
                {
                    this.target = target ?? throw new global::System.ArgumentNullException(nameof(target));
                }

                /// <inheritdoc />
                public override global::System.Threading.Tasks.ValueTask<{{WebView}}WebHostAnswer> InvokeAsync(string method, {{WebView}}WebHostArguments arguments)
                {
                    switch (method)
                    {

            """);
        foreach (var method in methods)
        {
            source.Append($"            case \"{JavaScriptName(method.Name)}\":\n");
            source.Append($"                {Call(method)}\n");
        }

        source.Append("""
                        default:
                            throw NoSuchMethod(method);
                    }
                }
            }

            """);
        return Footer(source, space);
    }

    private static string Bridge(string space, string access, string name, string type)
    {
        var source = Header(space);
        source.Append($$"""
            /// <summary>Adds a <see cref="{{type}}"/> to a WebView's bridge, for pages to call.</summary>
            [global::System.CodeDom.Compiler.GeneratedCode("Adamantium.UI.Generators", "1.0")]
            {{access}} static class {{name}}Bridge
            {
                /// <summary>Lets pages at <paramref name="origins"/> call <paramref name="target"/> as <c>adamantium.hostObjects.name</c>.</summary>
                public static void AddHostObject(this {{WebView}}WebViewBridge bridge, string name, {{type}} target, params string[] origins) =>
                    bridge.AddHostObject(name, new {{name}}(target), origins);

                /// <summary>Lets pages at <paramref name="origins"/> call <paramref name="target"/> as <c>adamantium.hostObjects.name</c>, its arguments and answers written by <paramref name="options"/>.</summary>
                public static void AddHostObject(this {{WebView}}WebViewBridge bridge, string name, {{type}} target, global::System.Text.Json.JsonSerializerOptions options, params string[] origins) =>
                    bridge.AddHostObject(name, new {{name}}(target), options, origins);
            }

            """);
        return Footer(source, space);
    }

    // The call of one method and its answer: a value at once, or once the method's task is done.
    private static string Call(IMethodSymbol method)
    {
        var arguments = string.Join(", ", method.Parameters.Select((parameter, index) =>
            parameter.Type.ToDisplayString() == "Adamantium.UI.WebView.IWebScriptFunction"
                ? $"arguments.Function({index})"
                : $"arguments.Get<{parameter.Type.ToDisplayString(Full)}>({index})"));
        var invocation = $"target.{method.Name}({arguments})";
        if (method.ReturnsVoid)
        {
            return $"{invocation};\n                return new({WebView}WebHostAnswer.None);";
        }

        var returned = method.ReturnType as INamedTypeSymbol;
        switch (returned?.OriginalDefinition.ToDisplayString())
        {
            case "System.Threading.Tasks.Task":
            case "System.Threading.Tasks.ValueTask":
                return $"return When({invocation});";
            case "System.Threading.Tasks.Task<TResult>":
            case "System.Threading.Tasks.ValueTask<TResult>":
                return $"return When({invocation}, result => {Answer(returned.TypeArguments[0], "result")});";
            default:
                return $"return new({Answer(method.ReturnType, invocation)});";
        }
    }

    // The answer that is value: an object of its own for an interface a page calls, JSON for the rest.
    private static string Answer(ITypeSymbol type, string value)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Interface } named &&
            named.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == AttributeName))
        {
            var space = named.ContainingNamespace.IsGlobalNamespace ? "global::" : "global::" + named.ContainingNamespace.ToDisplayString() + ".";
            return $"{WebView}WebHostAnswer.FromObject({value} is {{ }} given ? new {space}{HostObjectName(named)}(given) : null)";
        }

        return $"arguments.Answer<{type.ToDisplayString(Full)}>({value})";
    }

    private static Location Where(ISymbol member, INamedTypeSymbol contract) =>
        member.Locations.FirstOrDefault(location => location.IsInSource) ?? contract.Locations.FirstOrDefault();

    private static string JavaScriptName(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string HostObjectName(INamedTypeSymbol contract)
    {
        var name = contract.Name;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        return name + "HostObject";
    }

    private static string Hint(string space, string name) => (space == null ? name : space + "." + name) + ".g.cs";

    private static StringBuilder Header(string space)
    {
        var source = new StringBuilder("// <auto-generated/>\n#nullable disable\n\n");
        if (space != null)
        {
            source.Append($"namespace {space}\n{{\n");
        }

        return source;
    }

    private static string Footer(StringBuilder source, string space)
    {
        if (space != null)
        {
            source.Append("}\n");
        }

        return source.ToString();
    }
}
