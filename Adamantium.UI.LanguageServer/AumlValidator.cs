using System.Text.RegularExpressions;
using Adamantium.Core;
using Adamantium.UI.Markup;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.LanguageServer;

/// <summary>A diagnostic span (0-based line/character) and message for an AUML problem; <paramref name="Code"/> is the
/// build's id when the build reports it too.</summary>
public sealed record AumlDiagnostic(int Line, int Character, int Length, string Message, bool IsWarning = false, string Code = null);

/// <summary>Validates element, attribute and enum names in a well-formed AUML document against the project's types,
/// including property-element values. Flags only what definitely does not exist.</summary>
public static class AumlValidator
{
    public static IReadOnlyList<AumlDiagnostic> Validate(string text, AumlTypeModel model)
    {
        var diagnostics = new List<AumlDiagnostic>();

        // Undeclared well-known directive prefix (x: with no xmlns:x). Raw-text scan, because such a
        // prefix is invalid XML and the parser below would just throw past it — yet we still want a
        // diagnostic so the "Declare xmlns:x" quick-fix has something to anchor on.
        ValidateKnownPrefixes(text, diagnostics);

        AumlDocument document;
        try { document = AumlParser.Parse(text); }
        catch { return diagnostics; }            // malformed/incomplete (maybe the prefix above) — return what we have

        // Walk the tree whenever there is one — even a missing root xmlns leaves a usable AST, and a
        // per-element "not in scope" diagnostic (with an import quick-fix) beats a generic parse message.
        if (document.Root is AumlAstObjectNode root)
        {
            Walk(root, model, diagnostics, text, AumlNamespaces.Scan(text));
            ValidateClrNamespaces(text, model, diagnostics);
            ValidateTypeReferences(text, model, diagnostics);
        }

        // Surface raw AUML parse errors only when the walk found nothing better (avoids double-flagging
        // the root with both "xmlns missing" and "not in scope").
        if (diagnostics.Count == 0 && document.HasErrors && document.Logger?.Messages is { } messages)
            foreach (var message in messages)
                if (message.Type == LogMessageType.Error)
                    diagnostics.Add(new AumlDiagnostic(0, 0, 1, message.Text));

        return diagnostics;
    }

    // The x: directive prefix used as an element/attribute name (preceded by '<' or whitespace, so we
    // don't match "x:" inside quoted values), but not declared via xmlns:x.
    private static readonly Regex KnownPrefixUsage = new(@"(?<=[<\s])x:[A-Za-z_]", RegexOptions.Compiled);

    private static void ValidateKnownPrefixes(string text, List<AumlDiagnostic> diagnostics)
    {
        if (AumlNamespaces.Scan(text).ContainsKey("x")) return;   // declared -> nothing to flag

        foreach (Match match in KnownPrefixUsage.Matches(text))
        {
            var (line, character) = LineColAt(text, match.Index);
            diagnostics.Add(new AumlDiagnostic(line, character, 1, "Namespace prefix 'x' is not declared (add xmlns:x)"));
        }
    }

    private static readonly Regex ClrXmlnsDeclaration = new(
        @"xmlns(?::[\w.\-]+)?\s*=\s*""(?<uri>clr-namespace:[^""]*)""", RegexOptions.Compiled);

    /// <summary>
    /// Flags <c>clr-namespace:</c> xmlns declarations whose CLR namespace (and optional assembly)
    /// isn't found in the project's references — Rider's XML support can't validate these, so the
    /// language server does (the matching highlight filter suppresses XML's "URI is not registered").
    /// </summary>
    private static void ValidateClrNamespaces(string text, AumlTypeModel model, List<AumlDiagnostic> diagnostics)
    {
        foreach (Match match in ClrXmlnsDeclaration.Matches(text))
        {
            var uri = match.Groups["uri"];
            if (model.GetElements(uri.Value).Count > 0) continue;     // resolves to a real CLR namespace

            var (line, character) = LineColAt(text, uri.Index);
            diagnostics.Add(new AumlDiagnostic(line, character, uri.Length, $"CLR namespace not found: '{uri.Value}'"));
        }
    }

    private const string BuildCode = "AUM001";
    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Flags the type of a <c>{x:Type}</c> and the type and member of a <c>{x:Static}</c> that the build would not
    /// find, with the build's own message - found the same way: by the prefix's namespace, else by the short name.
    /// </summary>
    private static void ValidateTypeReferences(string text, AumlTypeModel model, List<AumlDiagnostic> diagnostics)
    {
        var namespaces = AumlNamespaces.Scan(text);
        var x = namespaces.FirstOrDefault(n => n.Value == AumlXDirectives.Xmlns).Key;
        if (string.IsNullOrEmpty(x))
        {
            return;
        }

        var reference = new Regex($@"\{{\s*{Regex.Escape(x)}:(?<directive>Type|Static)\s+(?<body>[^\s,=}}]+)\s*[,}}]");
        var uncommented = Comment.Replace(text, m => Regex.Replace(m.Value, @"[^\r\n]", " "));
        foreach (Match match in reference.Matches(uncommented))
        {
            var body = match.Groups["body"];
            var problem = match.Groups["directive"].Value == "Type"
                ? TypeProblem(body.Value, namespaces, model)
                : StaticProblem(body.Value, namespaces, model);
            if (problem != null)
            {
                var (line, character) = LineColAt(text, body.Index);
                diagnostics.Add(new AumlDiagnostic(line, character, body.Length, problem, Code: BuildCode));
            }
        }
    }

    private static string TypeProblem(string typeText, IReadOnlyDictionary<string, string> namespaces, AumlTypeModel model)
    {
        if (!model.TryResolveWritten(typeText, namespaces, out var type) || type != null)
        {
            return null;
        }

        var colon = typeText.IndexOf(':');
        var name = typeText[(colon + 1)..];
        return colon >= 0 && namespaces.TryGetValue(typeText[..colon], out var xmlns)
            ? $"Type {name} could not be found in namespace {ClrNamespaceOf(xmlns)}"
            : $"Type {name} could not be found in any linked assembly";
    }

    private static string StaticProblem(string body, IReadOnlyDictionary<string, string> namespaces, AumlTypeModel model)
    {
        var lastDot = body.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == body.Length - 1)
        {
            return $"x:Static expects 'Type.Member', got '{body}'";
        }

        var typeText = body[..lastDot];
        var memberName = body[(lastDot + 1)..];
        if (!model.TryResolveWritten(typeText, namespaces, out var type))
        {
            return null;
        }

        if (type == null)
        {
            return $"x:Static type '{typeText}' could not be resolved";
        }

        return type.GetMemberByName(memberName) != null ? null : $"x:Static: '{type.FullName}' has no member '{memberName}'";
    }

    private static string ClrNamespaceOf(string xmlns)
    {
        const string scheme = "clr-namespace:";
        if (!xmlns.StartsWith(scheme, StringComparison.Ordinal))
        {
            return xmlns;
        }

        var end = xmlns.IndexOf(';');
        return end < 0 ? xmlns[scheme.Length..] : xmlns[scheme.Length..end];
    }

    private static (int Line, int Character) LineColAt(string text, int offset)
    {
        int line = 0, character = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; character = 0; }
            else character++;
        }
        return (line, character);
    }

    private static void Walk(AumlAstObjectNode node, AumlTypeModel model, List<AumlDiagnostic> diagnostics, string text,
        IReadOnlyDictionary<string, string> namespaces)
    {
        var xmlns = node.TypeReference.Namespace;
        var name = node.TypeReference.Name;

        IResolvedType element = null;
        if (model.IsKnownNamespace(xmlns))
        {
            element = model.GetElement(xmlns, name);
            if (element is null)
                diagnostics.Add(At(node, name.Length, $"Unknown element '{name}'"));
        }
        else if (string.IsNullOrEmpty(xmlns) && model.FindElement(name) is not null)
        {
            // Unprefixed element whose xmlns isn't declared, but the type exists in a known namespace
            // (e.g. a deleted default xmlns) — the auto-import quick-fix offers to declare it.
            diagnostics.Add(At(node, name.Length, $"'{name}' is not in scope — its xmlns is not declared"));
        }

        foreach (var property in node.GetProperties())
        {
            if (property.Property is AumlAstPropertyReference reference)
            {
                if (element is not null && !reference.IsAttachedProperty)
                {
                    ValidateAttribute(reference, property, name, element, model, diagnostics);
                }

                ValidateTypeValues(reference, property, element, new TypeValueScan(model, text, namespaces, diagnostics));
            }

            // Descend into property-element values: <Setter.Value><ControlTemplate>... etc.
            foreach (var value in property.Values)
                if (value is AumlAstObjectNode nested)
                    Walk(nested, model, diagnostics, text, namespaces);
        }

        foreach (var child in node.GetLogicalChildrenObjects())
            Walk(child, model, diagnostics, text, namespaces);
    }

    private static void ValidateTypeValues(AumlAstPropertyReference reference, AumlAstPropertyNode property,
        IResolvedType element, TypeValueScan scan)
    {
        if (property.Values.Count != 1 || property.Values[0] is AumlAstObjectNode)
        {
            return;
        }

        var nameOffset = OffsetAt(scan.Text, reference.Line, reference.Position);
        var quote = nameOffset < 0 ? -1 : scan.Text.IndexOfAny(['"', '\''], nameOffset);
        if (quote < 0)
        {
            return;
        }

        scan.Cursor = quote + 1;
        var member = reference.IsAttachedProperty ? null : element?.GetMemberByName(reference.Name);
        ValidateTypeValue(member, property.Values[0], scan);
    }

    private static void ValidateTypeValue(IResolvedMember member, IAumlAstValueNode value, TypeValueScan scan)
    {
        switch (value)
        {
            case AumlAstMarkupExtensionNode extension:
            {
                var extensionName = extension.TypeReference?.Name ?? string.Empty;
                var extensionType = scan.Model.ResolveMarkupExtensionType(extensionName[(extensionName.IndexOf(':') + 1)..]);
                var positional = 0;
                foreach (var argument in extension.Arguments)
                {
                    var argumentName = argument.Name;
                    if (string.IsNullOrEmpty(argumentName))
                    {
                        argumentName = positional++ == 0 && extensionType != null
                            ? scan.Model.GetDefaultProperty(extensionType)?.Name
                            : null;
                    }
                    else
                    {
                        var written = Regex.Match(scan.Text[scan.Cursor..], $@"\b{Regex.Escape(argumentName)}\s*=");
                        if (written.Success)
                        {
                            scan.Cursor += written.Index + written.Length;
                        }
                    }

                    var argumentMember = argumentName == null ? null : extensionType?.GetMemberByName(argumentName);
                    ValidateTypeValue(argumentMember, argument.Value, scan);
                }

                break;
            }
            case AumlAstDirective { Name: AumlDirectives.Type, Value: AumlAstTextNode inner } when TakesType(member):
                CheckTypeValue(inner.Text.Trim(), member, scan, true);
                break;
            case AumlAstTextNode literal when TakesType(member):
                CheckTypeValue(literal.Text.Trim(), member, scan, false);
                break;
        }
    }

    private static bool TakesType(IResolvedMember member) => member?.MemberType?.FullName == "System.Type";

    private static void CheckTypeValue(string typeText, IResolvedMember member, TypeValueScan scan, bool unknownFlaggedElsewhere)
    {
        if (typeText.Length == 0)
        {
            return;
        }

        var offset = scan.Text.IndexOf(typeText, scan.Cursor, StringComparison.Ordinal);
        if (offset < 0)
        {
            return;
        }

        scan.Cursor = offset + typeText.Length;
        if (!scan.Model.TryResolveWritten(typeText, scan.Namespaces, out var type))
        {
            return;
        }

        string problem;
        if (type != null)
        {
            problem = member.TypeOfProblem(type);
        }
        else
        {
            problem = unknownFlaggedElsewhere ? null : TypeProblem(typeText, scan.Namespaces, scan.Model);
        }

        if (problem != null)
        {
            var (line, character) = LineColAt(scan.Text, offset);
            scan.Diagnostics.Add(new AumlDiagnostic(line, character, typeText.Length, problem, Code: BuildCode));
        }
    }

    private static int OffsetAt(string text, int line, int position)
    {
        var offset = 0;
        for (var current = 1; current < line; current++)
        {
            offset = text.IndexOf('\n', offset) + 1;
            if (offset == 0)
            {
                return -1;
            }
        }

        return Math.Min(text.Length, offset + Math.Max(0, position - 1));
    }

    private static void ValidateAttribute(AumlAstPropertyReference reference, AumlAstPropertyNode property,
        string elementName, IResolvedType element, AumlTypeModel model, List<AumlDiagnostic> diagnostics)
    {
        var propertyName = reference.Name;
        if (!model.IsKnownAttribute(element, propertyName))
        {
            diagnostics.Add(At(reference, propertyName.Length, $"Unknown property '{propertyName}' on '{elementName}'"));
            return;
        }

        // Enum value check; skip flags combos and numeric forms to avoid false positives.
        var propertyType = model.GetPropertyType(element, propertyName);
        var value = property.Values.FirstOrDefault();
        if (value is not null && value.IsTextNode()
            && MarkupValueChecks.Problem(propertyType?.FullName, value.GetTextValue()) is { } problem)
        {
            diagnostics.Add(At(reference, propertyName.Length, problem));
            return;
        }

        if (propertyType is { TypeKind: ResolvedTypeKind.Enum } && value is not null && value.IsTextNode())
        {
            var valueText = value.GetTextValue();
            if (valueText.Length > 0 && !valueText.Contains(',') && !valueText.All(char.IsDigit))
            {
                var allowed = model.GetValueCompletions(propertyType);
                if (!allowed.Contains(valueText))
                    diagnostics.Add(At(reference, propertyName.Length,
                        $"'{valueText}' is not a valid {propertyType.Name} (expected: {string.Join(", ", allowed)})"));
            }
        }
    }

    private static AumlDiagnostic At(AumlAstNode node, int length, string message) =>
        new(Math.Max(0, node.Line - 1), Math.Max(0, node.Position - 1), Math.Max(1, length), message);
}
