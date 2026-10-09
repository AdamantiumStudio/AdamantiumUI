using System.Text.RegularExpressions;
using Adamantium.Core;
using Adamantium.UI.Markup;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.Parsers;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.LanguageServer;

/// <summary>A diagnostic span (0-based line/character) and message for an AUML problem; <paramref name="Code"/> is the
/// build's id when the build reports it too.</summary>
public sealed record AumlDiagnostic(int Line, int Character, int Length, string Message, bool IsWarning = false, string Code = null);

/// <summary>Validates element, attribute and enum names in a well-formed AUML document against the project's types,
/// including property-element values. Flags only what definitely does not exist.</summary>
public static class AumlValidator
{
    /// <summary>What keeps the text from being read as AUML at all - XML that is not well-formed, an xmlns missing on the
    /// root - each where it is written; for a file the build cannot be run on, having no project.</summary>
    public static IReadOnlyList<AumlDiagnostic> ValidateReading(string text)
    {
        try
        {
            return AumlParser.Parse(text).Errors
                .Select(error => new AumlDiagnostic(Math.Max(0, (error.At?.Line ?? 1) - 1), Math.Max(0, (error.At?.Position ?? 1) - 1), 1, error.Message))
                .ToList();
        }
        catch (System.Xml.XmlException exception)
        {
            return [new AumlDiagnostic(Math.Max(0, exception.LineNumber - 1), Math.Max(0, exception.LinePosition - 1), 1, XmlProblem.MessageOf(exception))];
        }
    }

    /// <summary>What the editor flags that the build itself does not: an xmlns declaration that names nothing, at the
    /// declaration - the build fails only at an element that uses it.</summary>
    public static IReadOnlyList<AumlDiagnostic> ValidateBeyondBuild(string text, AumlTypeModel model)
    {
        var diagnostics = new List<AumlDiagnostic>();
        ValidateNamespaces(text, model, diagnostics);
        return diagnostics;
    }

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
            ValidateNamespaces(text, model, diagnostics);
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
            diagnostics.Add(new AumlDiagnostic(line, character, 1, ServerMessages.PrefixXUndeclared()));
        }
    }

    private static readonly Regex XmlnsDeclaration = new(
        @"xmlns(?::[\w.\-]+)?\s*=\s*""(?<uri>[^""]*)""", RegexOptions.Compiled);

    /// <summary>
    /// Flags xmlns declarations the build would not find: a <c>clr-namespace:</c> whose CLR namespace (and optional
    /// assembly) isn't in the project's references, or a URI no referenced assembly declares with [XmlnsDefinition].
    /// Rider's XML support can't validate these, so the language server does (the matching highlight filter suppresses
    /// XML's "URI is not registered").
    /// </summary>
    private static void ValidateNamespaces(string text, AumlTypeModel model, List<AumlDiagnostic> diagnostics)
    {
        foreach (Match match in XmlnsDeclaration.Matches(text))
        {
            var uri = match.Groups["uri"];
            if (uri.Value == AumlXDirectives.Xmlns || model.IsKnownNamespace(uri.Value)) continue;

            var (line, character) = LineColAt(text, uri.Index);
            diagnostics.Add(new AumlDiagnostic(line, character, uri.Length, uri.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)
                ? ServerMessages.ClrNamespaceNotFound(uri.Value)
                : MarkupMessages.XmlNamespaceNotFound(uri.Value)));
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
            ? MarkupMessages.TypeNotInNamespace(name, ClrNamespaceOf(xmlns))
            : MarkupMessages.TypeNotInAnyAssembly(name);
    }

    private static string StaticProblem(string body, IReadOnlyDictionary<string, string> namespaces, AumlTypeModel model)
    {
        var lastDot = body.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == body.Length - 1)
        {
            return MarkupMessages.StaticShape(body);
        }

        var typeText = body[..lastDot];
        var memberName = body[(lastDot + 1)..];
        if (!model.TryResolveWritten(typeText, namespaces, out var type))
        {
            return null;
        }

        if (type == null)
        {
            return MarkupMessages.StaticTypeNotResolved(typeText);
        }

        return type.GetMemberByName(memberName) != null ? null : MarkupMessages.StaticNoMember(type.FullName, memberName);
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
                diagnostics.Add(At(node, name.Length, ServerMessages.UnknownElement(name)));
        }
        else if (string.IsNullOrEmpty(xmlns) && model.FindElement(name) is not null)
        {
            // Unprefixed element whose xmlns isn't declared, but the type exists in a known namespace
            // (e.g. a deleted default xmlns) — the auto-import quick-fix offers to declare it.
            diagnostics.Add(At(node, name.Length, ServerMessages.ElementNotInScope(name)));
        }

        foreach (var property in node.GetProperties())
        {
            if (property.Property is AumlAstPropertyReference reference)
            {
                if (element is not null && !reference.IsAttachedProperty)
                {
                    ValidateAttribute(reference, property, name, element, model, diagnostics);
                }
                else if (reference.IsAttachedProperty && reference.OwnerType is { } ownerType &&
                         model.GetElement(ownerType.Namespace, ownerType.Name) is { } owner &&
                         owner.GetMemberByName("Set" + reference.Name) is null)
                {
                    diagnostics.Add(At(reference, ownerType.Name.Length + 1 + reference.Name.Length,
                        MarkupMessages.NotAttached(owner.Name, reference.Name, name)) with { Code = BuildCode });
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
            diagnostics.Add(At(reference, propertyName.Length, ServerMessages.UnknownProperty(propertyName, elementName)));
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
                        ServerMessages.InvalidEnumValue(valueText, propertyType.Name, string.Join(", ", allowed))));
            }
        }
    }

    private static AumlDiagnostic At(AumlAstNode node, int length, string message) =>
        new(Math.Max(0, node.Line - 1), Math.Max(0, node.Position - 1), Math.Max(1, length), message);
}
