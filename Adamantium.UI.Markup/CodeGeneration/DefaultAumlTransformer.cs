using System.Globalization;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.Exceptions;
using Adamantium.UI.Markup.Localization;
using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.Markup.CodeGeneration;

public class DefaultAumlTransformer : IAumlTransformer
{
    /// <summary>The language tables <c>{Localize}</c> can name besides the compiled ones: this project's, generated in the
    /// same build from its .alang files, so they are found and checked before they are in the compilation.</summary>
    public IReadOnlyCollection<LanguageTableShape> LanguageTables { get; set; } = [];

    private const string MarkupItemAttributeName = "Adamantium.UI.Core.MarkupItemAttribute";

    private const string ContentAttributeName = "Adamantium.UI.Core.ContentAttribute";

    private const string ContentWrapperAttributeName = "Adamantium.UI.Core.ContentWrapperAttribute";

    private const string TrimSurroundingWhitespaceAttributeName = "Adamantium.UI.Core.TrimSurroundingWhitespaceAttribute";

    private const string DataTemplateSetName = "DataTemplateSet";

    // {Localize Table, Key={Binding Kind}}: where a key known only at run time is read from.
    private const string KeyArgument = "Key";

    // {Localize Table={Binding Phrases}, Key={Binding Name}}: where a table known only at run time is read from.
    private const string TableArgument = "Table";

    private static string CollapsedSpaces(string text, bool trimStart, bool trimEnd)
    {
        var collapsed = new System.Text.StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                space = true;
                continue;
            }

            if (space && (collapsed.Length > 0 || !trimStart))
            {
                collapsed.Append(' ');
            }

            space = false;
            collapsed.Append(c);
        }

        if (space && !trimEnd && (collapsed.Length > 0 || !trimStart))
        {
            collapsed.Append(' ');
        }

        return collapsed.ToString();
    }

    // Tokens of a shorthand collection: commas or spaces, but only outside a markup extension, whose own arguments are
    // separated the same way ("Auto, {Binding A, Mode=OneWay}, *").
    private static IEnumerable<string> SplitShorthand(string text)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                if (text[i] == '{')
                {
                    depth++;
                    continue;
                }

                if (text[i] == '}')
                {
                    depth--;
                    continue;
                }

                if (depth > 0 || (text[i] != ',' && text[i] != ' '))
                {
                    continue;
                }
            }

            var token = text.Substring(start, i - start).Trim();
            start = i + 1;
            if (token.Length > 0)
            {
                yield return token;
            }
        }
    }

    public AumlMetadataContainer Transform(AumlDocument document, ITypeResolver typeResolver, IDiagnosticSink diagnostics)
    {
        var container = new AumlMetadataContainer(typeResolver)
        {
            RelativeFilePath = document.RelativeFilePath,
            SourceFilePath = document.SourceFilePath,
            AssemblyName = document.RootNamespace,
        };

        typeResolver.ScanXmlnsAttributes();
        foreach(var mapping in document.NamespaceMappings)
        {
            if (mapping.IsClrNamespace)
            {
                if (string.IsNullOrEmpty(mapping.Assembly))
                {
                    typeResolver.FindAssemblyByNamespace(mapping.Namespace);
                }
                else
                {
                    typeResolver.ResolveAssembly(mapping.Assembly);
                }
            }
        }

        void TransformTypeForElement(IAumlAstNode node)
        {
            switch (node)
            {
                case AumlAstObjectNode objectNode:
                    objectNode.TypeReference = ProcessTypeReference(objectNode.TypeReference, objectNode.GetLineInfo());
                    break;
                case AumlAstPropertyNode propertyNode:
                    if (propertyNode.Property is AumlAstPropertyReference reference)
                    {
                        reference.OwnerType = ProcessTypeReference(reference.OwnerType, reference.GetLineInfo());
                        if (reference.IsAttachedProperty)
                        {
                            reference.TargetType = ProcessTypeReference(reference.TargetType, reference.GetLineInfo());
                            ReportNotAttached(reference);
                        }
                        else
                        {
                            reference.TargetType = ResolvePropertyType(reference, reference.TargetType, reference.GetLineInfo());
                        }
                    }
                    break;
                case AumlAstPropertyReference propertyReference:
                    propertyReference.OwnerType = ProcessTypeReference(propertyReference.OwnerType, propertyReference.GetLineInfo());
                    if (propertyReference.IsAttachedProperty)
                    {
                        propertyReference.TargetType = ProcessTypeReference(propertyReference.TargetType, propertyReference.GetLineInfo());
                    }
                    else
                    {
                        propertyReference.TargetType = ResolvePropertyType(propertyReference, propertyReference.TargetType, propertyReference.GetLineInfo());
                    }
                    
                    break;
                case AumlAstMarkupExtensionNode markupExtension:
                    ProcessMarkupExtension(markupExtension);
                    break;
                //case AumlAstMarkupExtensionLiteral literal:
                //    literal.TypeReference = ProcessTypeReference(literal.TypeReference, literal.GetLineInfo());
                //    break;
            }
        }

        void ProcessMarkupExtension(IAumlAstMarkupExtensionNode markupExtension)
        {
            markupExtension.TypeReference = ProcessTypeReference(markupExtension.TypeReference, markupExtension.GetLineInfo());
            if (!markupExtension.TypeReference.IsResolved)
            {
                return;
            }

            var resolvedAssembly = typeResolver.GetResolvedAssembly(markupExtension.TypeReference.Assembly);

            if (resolvedAssembly == null)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.AssemblyNotFound(markupExtension.TypeReference.Assembly), markupExtension);
                return;           
            }
            
            var type = resolvedAssembly.GetTypeByFullName(markupExtension.TypeReference.GetFullTypeName());

            var positional = 0;
            foreach (var argument in markupExtension.Arguments)
            {
                IResolvedProperty property = null;
                var isDefault = false;
                if (string.IsNullOrEmpty(argument.Name))
                {
                    var result = type.FindPropertyWithAttribute("Adamantium.UI.Core.MarkupExtensions.DefaultPropertyAttribute", out property);
                    isDefault = positional++ == 0;
                }
                else
                {
                    property = type.GetAllProperties().FirstOrDefault(x => x.Name == argument.Name);
                }

                var takesType = property?.PropertyType?.FullName == "System.Type" && (isDefault || !string.IsNullOrEmpty(argument.Name));
                var transformedValue = ProcessValueNode(ReadTypeName(argument.Value, takesType));
                argument.Value = transformedValue;

                if (property == null)
                {
                    diagnostics.ReportError(document.FileName,
                        MarkupMessages.PropertyNotFoundIn(argument.Name, markupExtension.TypeReference.GetFullTypeName()),
                        argument as IAumlLineInfo ?? markupExtension);
                }
                else
                {
                    CheckTypeOf(type.GetMemberByName(property.Name), transformedValue);
                }

                if (transformedValue is AumlAstMarkupExtensionLiteral literal && property != null)
                {
                    literal.TypeReference = CreateResolved(property.PropertyType, markupExtension.GetLineInfo());
                }
            }
        }

        // The xml-namespace -> assembly registry only holds URIs registered via [XmlnsDefinition]. A property element
        // on a custom-namespace type (<local:TilesHost.ItemsPanel>) carries the raw clr-namespace declaration instead -
        // resolve that directly by CLR namespace (honoring an explicit ;assembly= part) so property elements work on
        // app-assembly controls, not only on framework types.
        IResolvedAssembly ResolveXmlDefinitionContainer(string xmlNamespace)
        {
            const string clrPrefix = "clr-namespace:";
            if (!xmlNamespace.StartsWith(clrPrefix, StringComparison.Ordinal))
                return typeResolver.GetResolvedAssemblyByXmlDefinition(xmlNamespace);

            var ns = xmlNamespace.Substring(clrPrefix.Length);
            var semi = ns.IndexOf(';');
            if (semi >= 0)
            {
                var assemblyName = ns.Substring(semi + 1).Replace("assembly=", string.Empty).Trim();
                ns = ns.Substring(0, semi);
                var byAssembly = typeResolver.ResolveAssembly(assemblyName);
                if (byAssembly != null) return byAssembly;
            }
            return typeResolver.FindAssemblyByNamespace(ns);
        }

        IAumlAstTypeReference ProcessTypeReference(IAumlAstTypeReference typeReference, IAumlLineInfo lineInfo)
        {
            if (typeReference == null || typeReference.IsResolved)
                return typeReference;

            if (typeReference.IsXmlNamespaceDeclaration)
            {
                if (string.IsNullOrEmpty(typeReference.Namespace))
                {
                    return ResolveByNameOnly(typeReference, lineInfo);
                }

                var typeContainer = ResolveXmlDefinitionContainer(typeReference.Namespace);

                if (typeContainer == null)
                {
                    diagnostics.ReportError(document.FileName, MarkupMessages.XmlNamespaceNotFound(typeReference.Namespace), lineInfo);
                    return typeReference;
                }

                var typeInfo = typeContainer.GetTypeByShortName(typeReference.Name);
                if (typeInfo == null)
                {
                    // A same-assembly (generated) type used WITHOUT a clr-namespace prefix - e.g. an embedded AUML view
                    // <ControlsView/> under the default xmlns. Such views live in the local assembly (pre-registered),
                    // not in a framework xmlns, so fall back to a short-name lookup before failing.
                    var local = typeResolver.ResolveByShortName(typeReference.Name);
                    if (local != null)
                        return CreateResolved(local, lineInfo);

                    diagnostics.ReportError(document.FileName, MarkupMessages.TypeNotInNamespace(typeReference.Name, typeReference.Namespace), lineInfo);
                    return typeReference;
                }

                return CreateResolved(typeInfo, lineInfo);
            }

            // not XmlNamespaceDeclaration
            if (string.IsNullOrEmpty(typeReference.Assembly))
            {
                if (string.IsNullOrEmpty(typeReference.Namespace))
                {
                    return ResolveByNameOnly(typeReference, lineInfo);
                }
                else
                {
                    return ResolveByFullTypeNameWithoutAssembly(typeReference, lineInfo);
                }
            }

            // CLR type reference
            var clrTypeContainer = typeResolver.ResolveAssembly(typeReference.Assembly);
            if (clrTypeContainer != null)
            {
                var typeInfo = clrTypeContainer.GetTypeByShortName(typeReference.Name);
                if (typeInfo == null)
                {
                    diagnostics.ReportError(document.FileName, MarkupMessages.TypeNotInNamespace(typeReference.Name, typeReference.Namespace), lineInfo);
                    return typeReference;
                }
                return CreateResolved(typeInfo, lineInfo);
            }

            throw new TypeNotAvailableException($"Type {typeReference.Name} is not available");
        }
        
        IAumlAstTypeReference ResolvePropertyType(AumlAstPropertyReference propertyReference, IAumlAstTypeReference typeReference, IAumlLineInfo lineInfo)
        {
            if (typeReference == null || typeReference.IsResolved)
                return typeReference;

            if (typeReference.IsXmlNamespaceDeclaration)
            {
                if (string.IsNullOrEmpty(typeReference.Namespace))
                {
                    return ResolveByNameOnly(typeReference, lineInfo);
                }

                var typeContainer = ResolveXmlDefinitionContainer(typeReference.Namespace);

                if (typeContainer == null)
                {
                    diagnostics.ReportError(document.FileName, MarkupMessages.XmlNamespaceNotFound(typeReference.Namespace), lineInfo);
                    return typeReference;
                }

                var typeInfo = typeContainer.GetTypeByShortName(typeReference.Name);
                if (typeInfo == null)
                {
                    // A same-assembly (generated) type used WITHOUT a clr-namespace prefix - e.g. a property set on an
                    // embedded AUML view <ControlsView VerticalAlignment="Center"/> under the default xmlns. The view
                    // lives in the local assembly (pre-registered), not in a framework xmlns, so fall back to a
                    // short-name lookup before failing (mirrors ProcessTypeReference).
                    typeInfo = typeResolver.ResolveByShortName(typeReference.Name);
                    if (typeInfo == null)
                    {
                        diagnostics.ReportError(document.FileName, MarkupMessages.TypeNotInNamespace(typeReference.Name, typeReference.Namespace), lineInfo);
                        return typeReference;
                    }
                }

                var propertyInfo = typeInfo.GetAllProperties().FirstOrDefault(x=>x.Name == propertyReference.Name);

                if (propertyInfo == null)
                {
                    diagnostics.ReportError(document.FileName, MarkupMessages.PropertyNotFoundIn(propertyReference.Name, typeReference.Name), lineInfo);
                    return typeReference;
                }

                return CreateResolved(propertyInfo.PropertyType, lineInfo);
            }

            return ProcessTypeReference(typeReference, lineInfo);
        }

        IAumlAstTypeReference ResolveByFullTypeNameWithoutAssembly(IAumlAstTypeReference typeReference, IAumlLineInfo lineInfo)
        {
            var typeInfo = typeResolver.FindAssemblyByNamespace(typeReference.Namespace);

            if (typeInfo == null)
            {
                diagnostics.ReportError(document.FileName, MarkupMessages.TypeNotInAnyAssembly(typeReference.Name), lineInfo);
                return typeReference;
            }
            
            var type = typeInfo.GetTypeByFullName(typeReference.GetFullTypeName());
            if (type == null)
            {
                diagnostics.ReportError(document.FileName, MarkupMessages.TypeNotInNamespace(typeReference.Name, typeReference.Namespace), lineInfo);
                return typeReference;
            }

            return CreateResolved(type, lineInfo);
        }

        IAumlAstTypeReference ResolveByNameOnly(IAumlAstTypeReference typeReference, IAumlLineInfo lineInfo)
        {
            var typeInfo = typeResolver.ResolveByShortName(typeReference.Name);

            if (typeInfo == null)
            {
                diagnostics.ReportError(document.FileName, MarkupMessages.TypeNotInAnyAssembly(typeReference.Name), lineInfo);
                return typeReference;
            }

            return CreateResolved(typeInfo, lineInfo);
        }

        IAumlAstTypeReference CreateResolved(IResolvedType typeInfo, IAumlLineInfo lineInfo)
        {
            bool isMarkupExtension = typeInfo.InheritsFromMarkupExtension(AumlParser.MarupExtensionClassFullName);

            return new AumlAstResolvedTypeReference(
                lineInfo,
                typeInfo.Namespace,
                typeInfo.Name,
                typeInfo.AssemblyName,
                isMarkupExtension
            );
        }
        
        IAumlAstValueNode ReadTypeName(IAumlAstValueNode value, bool takesType)
        {
            if (!takesType || value is not AumlAstTextNode text || string.IsNullOrWhiteSpace(text.Text))
            {
                return value;
            }

            var typeReference = MarkupExtensionParser.ParseTypeName(new ParserContext(null), text.Text, text.GetLineInfo(),
                document.NamespaceMappings.ToList());
            return new AumlAstTypeReferenceValueNode(text.GetLineInfo(), ProcessTypeReference(typeReference, text.GetLineInfo()));
        }

        void ReportMissingResourceKey(IAumlAstValueNode value, string propertyName)
        {
            if (value is not AumlAstMarkupExtensionNode { TypeReference.Name: "ResourceReference" or "ObservableResource" or "ThemeResource" } marker)
            {
                return;
            }

            var hasKey = marker.Arguments.Any(argument => (string.IsNullOrEmpty(argument.Name) || argument.Name == "Key")
                                                          && !string.IsNullOrWhiteSpace(argument.Value?.GetTextValue()));
            if (!hasKey)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.ResourceWithoutKey(marker.TypeReference.Name, propertyName, marker.Line, marker.Position), marker);
            }
        }

        void ReportNotAttached(AumlAstPropertyReference reference)
        {
            if (!reference.OwnerType.IsResolved || typeResolver.Resolve(reference.OwnerType.GetFullTypeName()) is not { } owner ||
                owner.Members.Any(m => m.Name == "Set" + reference.Name && m.MemberKind == ResolvedMemberKind.Method && m.IsStatic &&
                                       m.ParameterNames.Count == 2))
            {
                return;
            }

            diagnostics.ReportError(document.FileName,
                MarkupMessages.NotAttached(owner.Name, reference.Name, reference.ParentNode?.TypeReference?.Name), reference);
        }

        void ReportInvalidLiteral(IAumlAstValueNode value, AumlAstPropertyReference reference)
        {
            if (reference is not { IsAttachedProperty: false, TargetType.IsResolved: true } || value == null || !value.IsTextNode())
            {
                return;
            }

            var type = typeResolver.Resolve(reference.TargetType.GetFullTypeName());
            var text = value.GetTextValue()?.Trim() ?? string.Empty;
            var expected = type == null ? null : ExpectedLiteral(type, text);
            if (expected != null)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.InvalidLiteral(text, type.Name, reference.Name, expected, value.Line, value.Position), value);
            }
        }

        void CheckTypeOf(IResolvedMember member, IAumlAstValueNode value)
        {
            if (member == null || value is not AumlAstTypeReferenceValueNode { TypeReference.IsResolved: true } typeValue)
            {
                return;
            }

            var type = typeResolver.Resolve(typeValue.TypeReference.GetFullTypeName());
            if (member.TypeOfProblem(type) is { } problem)
            {
                diagnostics.ReportError(document.FileName, $"{problem} (line {value.Line}, position {value.Position}).", value);
            }
        }

        IAumlAstValueNode ProcessValueNode(IAumlAstValueNode valueNode)
        {
            switch (valueNode)
            {
                // A directive standing in value position, e.g. {x:Type ...}
                case AumlAstDirective directive:
                    return ProcessDirectiveValue(directive);

                case AumlAstMarkupExtensionNode { TypeReference.Name: "Localize" } localize:
                    return ResolveLocalize(localize);

                // An ordinary markup extension (not a directive)
                case AumlAstMarkupExtensionNode markupExtension:
                    ProcessMarkupExtension(markupExtension);
                    return markupExtension;

                // Text, or anything else - left as it stands
                default:
                    return valueNode;
            }
        }
        
        IAumlAstValueNode ProcessDirectiveValue(AumlAstDirective directive)
        {
            var directiveBody = (directive.Value as AumlAstTextNode)?.Text;

            switch (directive.Name)
            {
                // The one directive that carries no value: saying nothing IS its value.
                case AumlDirectives.Null:
                    return new AumlAstNullValueNode(directive.GetLineInfo());

                case AumlDirectives.Type:
                    if (MissingValue(directive, directiveBody)) return directive;

                    // Read the type name with the very mechanism the parser uses
                    var typeRef = MarkupExtensionParser.ParseTypeName(new ParserContext(null), directiveBody, directive.GetLineInfo(), document.NamespaceMappings.ToList());

                    // Resolve it to a concrete IResolvedType
                    var resolvedTypeRef = ProcessTypeReference(typeRef, directive.GetLineInfo());

                    // Hand back a node that carries the resolved type
                    return new AumlAstTypeReferenceValueNode(directive.GetLineInfo(), resolvedTypeRef);

                case AumlDirectives.Static:
                    if (MissingValue(directive, directiveBody)) return directive;

                    return ResolveStaticMember(directive, directiveBody);
               
                default:
                    if (MissingValue(directive, directiveBody)) return directive;

                    // A name the registry knows is not "unknown" - it is written in the wrong place, and saying so is
                    // the difference between "you invented this" and "this one goes on the element".
                    diagnostics.ReportError(document.FileName, AumlDirectives.Find(directive.Name) != null
                        ? MarkupMessages.DirectiveBelongsOnElement(directive.Name)
                        : MarkupMessages.UnknownDirective(directive.Name), directive);
                    return directive;
            }
        }

        // A type-valued directive accepts both the plain reference (local:Vm) and the markup-extension form
        // ({x:Type local:Vm}) - the parser turns the latter into a Type directive whose value is the inner text.
        string UnwrapTypeText(string text)
        {
            var trimmed = text.Trim();
            if (!trimmed.StartsWith("{")) return trimmed;

            var parsed = MarkupExtensionParser.Parse(new ParserContext(null), trimmed, document.Root.GetLineInfo(),
                document.NamespaceMappings.ToList());
            return parsed is AumlAstDirective { Name: AumlDirectives.Type, Value: AumlAstTextNode inner }
                ? inner.Text.Trim()
                : trimmed;
        }

        IAumlAstValueNode ResolveLocalize(AumlAstMarkupExtensionNode localize)
        {
            var text = localize.Arguments.FirstOrDefault(a => string.IsNullOrEmpty(a.Name))?.Value?.GetTextValue()?.Trim();
            var colon = text?.IndexOf(':') ?? -1;
            var prefix = colon > 0 ? text.Substring(0, colon) : string.Empty;
            var name = colon > 0 ? text.Substring(colon + 1) : text;
            var dot = name?.LastIndexOf('.') ?? -1;

            // A table read from a binding takes its key from one too: {Localize Table={Binding Phrases}, Key={Binding Name}}.
            var keySource = localize.Arguments.FirstOrDefault(a => a.Name == KeyArgument);
            var tableSource = localize.Arguments.FirstOrDefault(a => a.Name == TableArgument);
            if (tableSource != null)
            {
                return ResolveReadTable(localize, text, tableSource, keySource);
            }

            // A table alone takes its key from a binding: {Localize CanvasStrings, Key={Binding Sort}}.
            if (dot < 0 && !string.IsNullOrEmpty(name) && keySource != null)
            {
                return ResolveReadKey(localize, prefix, name, keySource);
            }

            if (dot <= 0 || dot == name.Length - 1)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.LocalizeShape(text), localize);
                return localize;
            }

            var table = name.Substring(0, dot);
            var key = name.Substring(dot + 1);
            var tableFullName = FindLanguageTable(prefix, table);
            if (tableFullName == null)
            {
                ReportNoTable(prefix, table, localize);
                return localize;
            }

            if (!TryPlaceholdersOf(tableFullName, key, out var placeholders))
            {
                diagnostics.ReportError(document.FileName, MarkupMessages.LocalizeNoString(table, key), localize);
                return localize;
            }

            var arguments = localize.Arguments.Where(a => !string.IsNullOrEmpty(a.Name)).ToList();
            if (placeholders != null && !SamePlaceholders(table, key, placeholders, arguments, localize))
            {
                return localize;
            }

            foreach (var argument in arguments)
            {
                argument.Value = ProcessValueNode(argument.Value);
            }

            return new AumlAstLocalizedStringNode(localize.GetLineInfo(), tableFullName, key, arguments);
        }

        // The key is known only when the application runs, so neither it nor the arguments can be checked here: a word
        // the table lacks is said as it is, and a word takes the arguments it uses.
        IAumlAstValueNode ResolveReadKey(AumlAstMarkupExtensionNode localize, string prefix, string table,
            IAumlAstMarkupExtensionArgument keySource)
        {
            var tableFullName = FindLanguageTable(prefix, table);
            if (tableFullName == null)
            {
                ReportNoTable(prefix, table, localize);
                return localize;
            }

            if (!IsFollowed(keySource.Value))
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.LocalizeKeyFromBinding(table, KeyArgument), localize);
                return localize;
            }

            var arguments = localize.Arguments.Where(a => !string.IsNullOrEmpty(a.Name) && a.Name != KeyArgument).ToList();
            foreach (var argument in arguments)
            {
                argument.Value = ProcessValueNode(argument.Value);
            }

            return new AumlAstLocalizedStringNode(localize.GetLineInfo(), tableFullName, null, arguments,
                ProcessValueNode(keySource.Value));
        }

        // Neither the table nor the key is known before the application runs - a thing that brings its own words, as
        // a module loaded from a file does - so nothing past their bindings can be checked here.
        IAumlAstValueNode ResolveReadTable(AumlAstMarkupExtensionNode localize, string named,
            IAumlAstMarkupExtensionArgument tableSource, IAumlAstMarkupExtensionArgument keySource)
        {
            if (!string.IsNullOrEmpty(named))
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.LocalizeTableTwice(named, TableArgument), localize);
                return localize;
            }

            if (!IsFollowed(tableSource.Value) || keySource == null || !IsFollowed(keySource.Value))
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.LocalizeBothFromBinding(TableArgument, KeyArgument), localize);
                return localize;
            }

            var arguments = localize.Arguments
                .Where(a => !string.IsNullOrEmpty(a.Name) && a.Name != KeyArgument && a.Name != TableArgument)
                .ToList();
            foreach (var argument in arguments)
            {
                argument.Value = ProcessValueNode(argument.Value);
            }

            return new AumlAstLocalizedStringNode(localize.GetLineInfo(), null, null, arguments,
                ProcessValueNode(keySource.Value), ProcessValueNode(tableSource.Value));
        }

        static bool IsFollowed(IAumlAstValueNode value) =>
            value is AumlAstMarkupExtensionNode { TypeReference.Name: "Binding" or "MultiBinding" or "TemplateBinding" };

        void ReportNoTable(string prefix, string table, AumlAstMarkupExtensionNode localize) =>
            diagnostics.ReportError(document.FileName,
                MarkupMessages.LocalizeNoTable((prefix.Length > 0 ? prefix + ":" : string.Empty) + table, table), localize);

        string FindLanguageTable(string prefix, string table)
        {
            if (prefix.Length > 0)
            {
                var mapping = document.NamespaceMappings.FirstOrDefault(m => m.Prefix == prefix);
                if (mapping == null)
                {
                    return null;
                }

                if (mapping.IsClrNamespace)
                {
                    var fullName = $"{mapping.Namespace}.{table}";
                    return LanguageTables.Any(t => t.FullName == fullName) || IsLanguageTable(typeResolver.Resolve(fullName)) ? fullName : null;
                }

                var byUri = typeResolver.GetResolvedAssemblyByXmlDefinition(mapping.Namespace)?.Types
                    .FirstOrDefault(t => t.Name == table && IsLanguageTable(t));
                return byUri?.FullName;
            }

            var own = LanguageTables.Where(t => t.FullName == table || t.FullName.EndsWith("." + table, StringComparison.Ordinal)).ToList();
            if (own.Count == 1)
            {
                return own[0].FullName;
            }

            var referenced = typeResolver.ResolveByShortName(table);
            return IsLanguageTable(referenced) ? referenced.FullName : null;
        }

        // The placeholders the string fills: from the table's shape, or from the compiled table's member; null when the
        // table is neither, so nothing can be checked. False when the table is known and has no such string.
        bool TryPlaceholdersOf(string tableFullName, string key, out IReadOnlyList<string> placeholders)
        {
            if (LanguageTables.FirstOrDefault(t => t.FullName == tableFullName) is { } shape)
            {
                return shape.Strings.TryGetValue(key, out placeholders);
            }

            placeholders = null;
            if (typeResolver.Resolve(tableFullName) is not { } compiled)
            {
                return true;
            }

            var member = compiled.GetMemberByName(key);
            placeholders = member?.ParameterNames;
            return member != null;
        }

        // Every placeholder filled, and nothing filled that is not one: a missing one would show as nothing.
        bool SamePlaceholders(string table, string key, IReadOnlyList<string> placeholders,
            IReadOnlyList<IAumlAstMarkupExtensionArgument> arguments, AumlAstMarkupExtensionNode localize)
        {
            var given = arguments.Select(a => a.Name).ToList();
            var unknown = given.Where(n => !placeholders.Contains(n)).ToList();
            var missing = placeholders.Where(p => !given.Contains(p)).ToList();
            if (unknown.Count == 0 && missing.Count == 0)
            {
                return true;
            }

            var fills = placeholders.Count == 0
                ? MarkupMessages.PlaceholdersNone()
                : MarkupMessages.PlaceholdersFilled(string.Join(", ", placeholders));
            var problem = unknown.Count > 0
                ? MarkupMessages.PlaceholderUnknown(table, key, string.Join(", ", unknown.Select(n => $"'{n}'")), fills)
                : MarkupMessages.PlaceholderMissing(table, key, string.Join(", ", missing), fills);
            diagnostics.ReportError(document.FileName, problem, localize);
            return false;
        }

        static bool IsLanguageTable(IResolvedType type) =>
            type != null && type.InheritsFrom("Adamantium.UI.Core.Localization.LocalizedStrings");

        // {x:Static prefix:Type.Member}: the type is named the way x:Type names one, the member is the last segment. The
        // member is checked HERE - a name that does not exist is a build error, not a silently empty property.
        IAumlAstValueNode ResolveStaticMember(AumlAstDirective directive, string body)
        {
            var lastDot = body.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == body.Length - 1)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.StaticShape(body), directive);
                return directive;
            }

            var typeText = body.Substring(0, lastDot);
            var memberName = body.Substring(lastDot + 1);

            var typeRef = MarkupExtensionParser.ParseTypeName(new ParserContext(null), typeText, directive.GetLineInfo(),
                document.NamespaceMappings.ToList());
            var resolvedRef = ProcessTypeReference(typeRef, directive.GetLineInfo());
            if (resolvedRef is not { IsResolved: true })
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.StaticTypeNotResolved(typeText), directive);
                return directive;
            }

            var owner = typeResolver.Resolve(resolvedRef.GetFullTypeName());
            if (owner?.GetMemberByName(memberName) == null)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.StaticNoMember(resolvedRef.GetFullTypeName(), memberName), directive);
                return directive;
            }

            return new AumlAstStaticMemberValueNode(directive.GetLineInfo(), resolvedRef, memberName);
        }

        // Every x: name is judged against the registry, so a directive nobody implemented - or one written in the wrong
        // place - says so at build time instead of being silently dropped. The registry is what tooling completes from,
        // so this is also what keeps the two from drifting apart.
        void ReportIfNotAnAttributeDirective(AumlAstDirective directive)
        {
            var known = AumlDirectives.Find(directive.Name);
            if (known is { Usage: AumlDirectiveUsage.Attribute })
            {
                return;
            }

            diagnostics.ReportError(document.FileName, known != null
                ? MarkupMessages.DirectiveBelongsInValue(directive.Name)
                : MarkupMessages.UnknownDirective(directive.Name), directive);
        }

        bool MissingValue(AumlAstDirective directive, string body)
        {
            if (!string.IsNullOrWhiteSpace(body)) return false;

            diagnostics.ReportError(document.FileName, MarkupMessages.DirectiveMissingValue(directive.Name), directive);
            return true;
        }

        // A [MarkupItem] collection written as the shorthand string with a markup extension among the tokens -
        // ColumnDefinitions="Auto,{TemplateBinding OverflowButtonWidth}" - becomes the element form the rest of the
        // pipeline already understands: one item object per token, each with the declared item property set. A plain
        // string keeps going to the TypeParser untouched.
        void ExpandShorthandCollection(AumlAstPropertyNode propertyNode)
        {
            if (propertyNode.Property is not AumlAstPropertyReference { IsAttachedProperty: false } reference) return;
            if (reference.TargetType is not { IsResolved: true }) return;
            if (propertyNode.Values.Count != 1 || propertyNode.Values[0] is not AumlAstTextNode text) return;
            if (text.Text.IndexOf('{') < 0) return;

            var markupItem = typeResolver.Resolve(reference.TargetType.GetFullTypeName())?.GetAttribute(MarkupItemAttributeName);
            if (markupItem == null) return;

            markupItem.NamedArguments.TryGetValue("ItemType", out var itemTypeArgument);
            markupItem.NamedArguments.TryGetValue("ItemProperty", out var itemPropertyArgument);
            var itemPropertyName = itemPropertyArgument?.ToString();

            var itemType = itemTypeArgument == null ? null : typeResolver.Resolve(itemTypeArgument.ToString());
            var itemProperty = itemType?.GetAllProperties().FirstOrDefault(x => x.Name == itemPropertyName);
            if (itemProperty == null)
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.MarkupItemNoProperty(reference.TargetType.GetFullTypeName()), propertyNode);
                return;
            }

            var lineInfo = text.GetLineInfo();
            var itemTypeReference = CreateResolved(itemType, lineInfo);
            var itemPropertyTypeReference = CreateResolved(itemProperty.PropertyType, lineInfo);
            var items = new List<IAumlAstValueNode>();

            foreach (var token in SplitShorthand(text.Text))
            {
                var itemNode = new AumlAstObjectNode(lineInfo, itemTypeReference);
                var value = token.StartsWith("{")
                    ? MarkupExtensionParser.Parse(new ParserContext(null), token, lineInfo, document.NamespaceMappings.ToList())
                    : new AumlAstTextNode(lineInfo, token);

                itemNode.Children.Add(new AumlAstPropertyNode(
                    lineInfo,
                    new AumlAstPropertyReference(lineInfo, false, itemNode, itemTypeReference, itemPropertyTypeReference, itemPropertyName),
                    value));

                items.Add(itemNode);
            }

            propertyNode.Values = items;
        }


        IResolvedType ResolvedTypeOf(IAumlAstTypeReference reference) =>
            reference is { IsResolved: true } ? typeResolver.Resolve(reference.GetFullTypeName()) : null;

        IResolvedType WrapperOf(IResolvedType collection) =>
            collection?.GetAttribute(ContentWrapperAttributeName)?.NamedArguments is { } arguments &&
            arguments.TryGetValue("WrapperType", out var wrapper) && wrapper != null
                ? typeResolver.Resolve(wrapper.ToString())
                : null;

        List<T> WithoutSpaces<T>(List<T> nodes) where T : class, IAumlAstNode =>
            nodes.Where(node => node is not AumlAstTextNode text || !ParserContext.IsXmlSpace(text.Text)).ToList();

        bool IsContent(IAumlAstNode node) =>
            node is AumlAstObjectNode || (node is AumlAstTextNode text && !ParserContext.IsXmlSpace(text.Text));

        List<T> Wrapped<T>(List<T> nodes, IResolvedType wrapper) where T : class, IAumlAstNode
        {
            if (!wrapper.FindPropertyWithAttribute(ContentAttributeName, out var wrapperContent))
            {
                return WithoutSpaces(nodes);
            }

            var first = nodes.FindIndex(IsContent);
            var last = nodes.FindLastIndex(IsContent);
            var result = new List<T>(nodes.Count);
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not AumlAstTextNode text)
                {
                    result.Add(nodes[i]);
                    continue;
                }

                var words = CollapsedSpaces(text.Text, first < 0 || i <= first || TrimsSpace(nodes, i - 1),
                    i >= last || TrimsSpace(nodes, i + 1));
                if (words.Length == 0)
                {
                    continue;
                }

                var info = text.GetLineInfo();
                var wrapperReference = CreateResolved(wrapper, info);
                var item = new AumlAstObjectNode(info, wrapperReference);
                item.Children.Add(new AumlAstPropertyNode(info,
                    new AumlAstPropertyReference(info, false, item, wrapperReference,
                        CreateResolved(wrapperContent.PropertyType, info), wrapperContent.Name),
                    new AumlAstTextNode(info, words)));
                result.Add(item as T);
            }

            return result;
        }

        bool TrimsSpace<T>(List<T> nodes, int index) where T : class, IAumlAstNode =>
            index >= 0 && index < nodes.Count && nodes[index] is AumlAstObjectNode { TypeReference: { } neighbor } &&
            (neighbor.IsResolved ? ResolvedTypeOf(neighbor) : typeResolver.ResolveByShortName(neighbor.Name))?
            .HasAttribute(TrimSurroundingWhitespaceAttributeName) == true;

        void WrapText(AumlAstObjectNode objectNode)
        {
            if (!objectNode.Children.OfType<AumlAstTextNode>().Any())
            {
                return;
            }

            var content = ResolvedTypeOf(objectNode.TypeReference) is { } type &&
                          type.FindPropertyWithAttribute(ContentAttributeName, out var found)
                ? found
                : null;
            List<IAumlAstNode> children;
            if (content?.PropertyType?.SpecialType == ResolvedSpecialType.System_String &&
                objectNode.Children.Any(IsContent))
            {
                var texts = objectNode.Children.OfType<AumlAstTextNode>().ToList();
                var info = texts[0].GetLineInfo();
                children = objectNode.Children.Where(child => child is not AumlAstTextNode).ToList();
                children.Add(new AumlAstPropertyNode(info,
                    new AumlAstPropertyReference(info, false, objectNode, objectNode.TypeReference,
                        CreateResolved(content.PropertyType, info), content.Name),
                    new AumlAstTextNode(info, CollapsedSpaces(string.Concat(texts.Select(text => text.Text)), true, true))));
            }
            else
            {
                children = WrapperOf(content?.PropertyType) is { } wrapper
                    ? Wrapped(objectNode.Children, wrapper)
                    : WithoutSpaces(objectNode.Children);
            }

            objectNode.Children.Clear();
            objectNode.Children.AddRange(children);
        }

        var entityType = EntityType.Unknown;
        var usings = new Dictionary<string, string>();

        var node = document.Root;
        TransformTypeForElement(node);
        container.RootClassName = container.FileName;
                
        var rootType = typeResolver.Resolve(node.TypeReference.GetFullTypeName());
        if (rootType == null)
        {
            diagnostics.ReportError(document.FileName,
                MarkupMessages.TypeNotFoundCheckNamespace(node.TypeReference.GetFullTypeName()), node);
            return container;
        }
                
        // Every root's tree is walked, fragments included, so previews report what the build would; whether a class is
        // generated is decided later from RootEntityType.
        entityType = rootType.EntityType;
        container.RootEntityType = entityType;

        var queue = new Queue<IAumlAstNode>();
        queue.Enqueue(document.Root);
            
        while (queue.Count > 0)
        {
            var element = queue.Dequeue();
            TransformTypeForElement(element);
            switch (element)
            {
                case AumlAstObjectNode objectNode:
                    WrapText(objectNode);
                    foreach (var child in objectNode.Children)
                    {
                        queue.Enqueue(child);
                    }
                    break;
                case AumlAstPropertyNode propertyNode:

                    ExpandShorthandCollection(propertyNode);

                    var reference = propertyNode.Property as AumlAstPropertyReference;
                    if (propertyNode.Values.OfType<AumlAstTextNode>().Any())
                    {
                        if (reference is { IsAttachedProperty: false } &&
                            WrapperOf(ResolvedTypeOf(reference.TargetType)) is { } wrapper)
                        {
                            propertyNode.Values = Wrapped(propertyNode.Values, wrapper);
                        }
                        else if (propertyNode.Values.Count > 1)
                        {
                            propertyNode.Values = WithoutSpaces(propertyNode.Values);
                        }
                    }

                    var takesType = reference is { IsAttachedProperty: false, TargetType.IsResolved: true }
                                    && reference.TargetType.GetFullTypeName() == "System.Type";
                    for (int i = 0; i < propertyNode.Values.Count; i++)
                    {
                        var transformedValue = ProcessValueNode(ReadTypeName(propertyNode.Values[i], takesType));
                        propertyNode.Values[i] = transformedValue;
                        ReportMissingResourceKey(transformedValue, reference?.Name);
                        ReportInvalidLiteral(transformedValue, reference);
                        queue.Enqueue(transformedValue);
                    }

                    if (takesType)
                    {
                        var owner = typeResolver.Resolve(reference.OwnerType.GetFullTypeName());
                        CheckTypeOf(owner?.GetMemberByName(reference.Name), propertyNode.Values[0]);
                    }

                    break;
                case AumlAstDirective directive:
                    // A directive written in VALUE position carries no parent (see MarkupExtensionParser) and has already
                    // been answered for by ProcessDirectiveValue; only the attribute form is judged here.
                    if (directive.ParentNode != null)
                    {
                        ReportIfNotAnAttributeDirective(directive);
                    }

                    if (directive.Name == AumlDirectives.Name)
                    {
                        if (directive.Value is AumlAstTextNode textNode)
                        {
                            container.NamedElements.Add(new NamedElement(textNode.Text, directive.ParentNode));
                        }
                    }
                    else if (directive.Name == AumlDirectives.KeepAlive && directive.ParentNode == document.Root)
                    {
                        var mode = (directive.Value as AumlAstTextNode)?.Text?.Trim();
                        var modes = AumlDirectives.Find(AumlDirectives.KeepAlive).Values;
                        if (mode != null && modes.Contains(mode))
                        {
                            container.RootKeepAlive = mode;
                        }
                        else
                        {
                            diagnostics.ReportError(document.FileName,
                                MarkupMessages.KeepAliveExpects(string.Join(", ", modes), mode), directive);
                        }
                    }
                    else if (directive.Name == AumlDirectives.Load)
                    {
                        // The condition may be a BINDING, so the value is not judged as text. A "{...}" body is parsed
                        // as a markup extension and walked like any other value, so its type reference resolves and the
                        // generator emits it through the ordinary binding path - there is no second way to write one.
                        var condition = (directive.Value as AumlAstTextNode)?.Text?.Trim();
                        if (condition != null && condition.StartsWith("{"))
                        {
                            var parsed = MarkupExtensionParser.Parse(new ParserContext(null), condition,
                                directive.GetLineInfo(), document.NamespaceMappings.ToList());
                            if (parsed is AumlAstMarkupExtensionNode { TypeReference.Name: "Binding" or "MultiBinding" })
                            {
                                directive.Value = ProcessValueNode(parsed);
                                queue.Enqueue(directive.Value);
                            }
                            else
                            {
                                diagnostics.ReportError(document.FileName,
                                    MarkupMessages.LoadExpects(condition), directive);
                            }
                        }
                        else if (condition == "True")
                        {
                            // Legal, and does nothing: the element is built either way. Worth saying, because a
                            // directive that reads as "I arranged something here" and arranges nothing is exactly what
                            // nobody notices - and it still costs a slot and turns the name into an accessor.
                            diagnostics.ReportWarning(document.FileName,
                                MarkupMessages.LoadTrueHoldsNothing(), directive);
                        }
                        else if (condition != "False")
                        {
                            diagnostics.ReportError(document.FileName,
                                MarkupMessages.LoadExpects(condition), directive);
                        }
                    }
                    else if (directive.Name == AumlDirectives.DataType)
                    {
                        // Declared, not inferred: the type tooling resolves {Binding} paths against inside the template, and
                        // the one a DataTemplateSet picks the template by. The name must resolve, so a renamed model does not
                        // leave a template silently pointing at nothing; the resolved type replaces the text for the builders.
                        if (directive.Value is AumlAstTextNode dataTypeNode && !string.IsNullOrWhiteSpace(dataTypeNode.Text))
                        {
                            var dataTypeRef = MarkupExtensionParser.ParseTypeName(new ParserContext(null),
                                UnwrapTypeText(dataTypeNode.Text), directive.GetLineInfo(), document.NamespaceMappings.ToList());
                            if (ProcessTypeReference(dataTypeRef, directive.GetLineInfo()) is { IsResolved: true } resolvedDataType)
                            {
                                directive.Value = new AumlAstTypeReferenceValueNode(directive.GetLineInfo(), resolvedDataType);
                            }
                            else
                            {
                                diagnostics.ReportError(document.FileName,
                                    MarkupMessages.DataTypeNotResolved(dataTypeNode.Text), directive);
                            }
                        }
                    }
                    else if (directive.Name == AumlDirectives.ViewModel && directive.ParentNode == document.Root)
                    {
                        // x:ViewModel="prefix:Vm" on the root records the view-model type as metadata; the framework
                        // resolves it from the DI container and assigns it as DataContext when the view goes live.
                        if (directive.Value is AumlAstTextNode vmNode && !string.IsNullOrWhiteSpace(vmNode.Text))
                        {
                            // Accept both a plain type reference (x:ViewModel="local:Vm") and the x:Type markup-extension
                            // form (x:ViewModel="{x:Type local:Vm}"). The parser turns {x:Type body} into a 'Type'
                            // directive whose value is the inner type text.
                            var typeText = vmNode.Text.Trim();
                            if (typeText.StartsWith("{"))
                            {
                                var parsed = MarkupExtensionParser.Parse(new ParserContext(null), typeText, directive.GetLineInfo(), document.NamespaceMappings.ToList());
                                if (parsed is AumlAstDirective { Name: AumlDirectives.Type, Value: AumlAstTextNode innerType })
                                    typeText = innerType.Text.Trim();
                            }

                            var vmTypeRef = MarkupExtensionParser.ParseTypeName(new ParserContext(null), typeText, directive.GetLineInfo(), document.NamespaceMappings.ToList());
                            var resolvedVmType = ProcessTypeReference(vmTypeRef, directive.GetLineInfo());
                            if (resolvedVmType is { IsResolved: true })
                            {
                                container.RootViewModelTypeName = resolvedVmType.GetFullTypeName();
                            }
                            else
                            {
                                diagnostics.ReportError(document.FileName, MarkupMessages.ViewModelNotResolved(vmNode.Text), directive);
                            }
                        }
                    }
                    else if (directive.Name == AumlDirectives.CreateInDesignTime && directive.ParentNode == document.Root)
                    {
                        container.RootCreateInDesignTime = directive.Value is AumlAstTextNode createNode
                            && bool.TryParse(createNode.Text.Trim(), out var create) && create;
                    }
                    break;
                    
                case AumlAstMarkupExtensionNode markupExtensionNode:
                    foreach (var ext in markupExtensionNode.Arguments)
                    {
                        if (ext.Value is AumlAstMarkupExtensionLiteral literal)
                        {
                            queue.Enqueue(literal);
                        }
                        else if (ext.Value is AumlAstMarkupExtensionNode extNode)
                        {
                            queue.Enqueue(extNode);
                        }
                        else if (ext.Value is AumlAstObjectNode objectNode)
                        {
                            queue.Enqueue(objectNode);
                        }
                    }
                    break;
            }
        }

        ReportTargetsIntoHeldBackElements(document, diagnostics);
        ReportTemplateSetConflicts(document, diagnostics);
        new MarkupStructureCheck(typeResolver, diagnostics, document.FileName, container.RootViewModelTypeName).Run(document.Root);

        foreach (var kvp in usings)
        {
            container.Usings.Add(kvp.Key);
        }

        foreach (var named in container.NamedElements)
        {
            container.NamedElementsMap.Add(named.Element, named.Name);
        }

        container.RootNode = document.Root;
        container.HasSemanticErrors = diagnostics.HasErrors;

        return container;
    }

    internal static string ExpectedLiteral(IResolvedType type, string text)
    {
        if (type.TypeKind == ResolvedTypeKind.Enum)
        {
            var names = type.Members
                .Where(m => m.MemberKind == ResolvedMemberKind.Field && m.Name != "value__")
                .Select(m => m.Name)
                .ToList();
            var parts = text.Split(',', '|').Select(p => p.Trim()).ToList();
            var valid = parts.All(p => names.Contains(p) || (p.Length > 0 && long.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)));
            return valid ? null : string.Join(", ", names);
        }

        switch (type.SpecialType)
        {
            case ResolvedSpecialType.System_Boolean:
                return bool.TryParse(text, out _) ? null : "true, false";
            case ResolvedSpecialType.System_Double:
            case ResolvedSpecialType.System_Single:
            case ResolvedSpecialType.System_Decimal:
                var special = type.SpecialType != ResolvedSpecialType.System_Decimal
                              && text is "Infinity" or "+Infinity" or "-Infinity" or "NaN" or "Auto";
                return special || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? null : MarkupMessages.ExpectedNumber();
            case ResolvedSpecialType.System_SByte:
            case ResolvedSpecialType.System_Int16:
            case ResolvedSpecialType.System_Int32:
            case ResolvedSpecialType.System_Int64:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? null : MarkupMessages.ExpectedWholeNumber();
            case ResolvedSpecialType.System_Byte:
            case ResolvedSpecialType.System_UInt16:
            case ResolvedSpecialType.System_UInt32:
            case ResolvedSpecialType.System_UInt64:
                return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? null : MarkupMessages.ExpectedNonNegativeWholeNumber();
            default:
                return null;
        }
    }

    // A trigger reaches a template part by NAME, and a name resolves through the names the template registered - which a
    // held-back element does only once it is built. So a trigger aimed into an unloaded chunk does not fail, it simply
    // never arrives, and the part keeps whatever it was given last. Say it at build time; the runtime cannot.
    private static void ReportTargetsIntoHeldBackElements(AumlDocument document, IDiagnosticSink diagnostics)
    {
        var heldBack = new HashSet<string>();
        var targets = new List<AumlAstTextNode>();
        Collect(document.Root);

        foreach (var target in targets)
        {
            if (heldBack.Contains(target.Text.Trim()))
            {
                diagnostics.ReportError(document.FileName,
                    MarkupMessages.TargetNameHeldBack(target.Text), target);
            }
        }

        void Collect(IAumlAstNode node)
        {
            switch (node)
            {
                case AumlAstObjectNode obj:
                    var directives = obj.Children.OfType<AumlAstDirective>().ToList();
                    if (directives.Any(d => d.Name == AumlDirectives.Load)
                        && directives.FirstOrDefault(d => d.Name == AumlDirectives.Name)?.Value is AumlAstTextNode name)
                    {
                        heldBack.Add(name.Text.Trim());
                    }

                    foreach (var child in obj.Children)
                    {
                        Collect(child);
                    }

                    break;

                case AumlAstPropertyNode property:
                    if ((property.Property as AumlAstPropertyReference)?.Name == "TargetName")
                    {
                        targets.AddRange(property.Values.OfType<AumlAstTextNode>());
                    }

                    foreach (var value in property.Values)
                    {
                        Collect(value);
                    }

                    break;
            }
        }
    }

    private static void ReportTemplateSetConflicts(AumlDocument document, IDiagnosticSink diagnostics)
    {
        Collect(document.Root);

        void Collect(IAumlAstNode node)
        {
            switch (node)
            {
                case AumlAstObjectNode obj:
                    if (obj.TypeReference?.Name == DataTemplateSetName)
                    {
                        Check(obj);
                    }

                    foreach (var child in obj.Children)
                    {
                        Collect(child);
                    }

                    break;

                case AumlAstPropertyNode property:
                    foreach (var value in property.Values)
                    {
                        Collect(value);
                    }

                    break;
            }
        }

        void Check(AumlAstObjectNode set)
        {
            var fallbacks = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in set.GetLogicalChildrenObjects())
            {
                var dataType = template.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.DataType);
                if (dataType == null)
                {
                    if (++fallbacks == 2)
                    {
                        diagnostics.ReportError(document.FileName,
                            MarkupMessages.DataTemplateSetTwoDefaults(), template);
                    }

                    continue;
                }

                if (dataType.Value is AumlAstTypeReferenceValueNode { TypeReference: { } type } && !seen.Add(type.GetFullTypeName()))
                {
                    diagnostics.ReportError(document.FileName,
                        MarkupMessages.DataTemplateSetTwoForType(type.Name), template);
                }
            }
        }
    }

    /// <summary>Registers the project's own language tables (<see cref="LanguageTables"/>) as the types they are
    /// generated into, so markup can name one before it is compiled: <c>{x:Static CanvasStrings.Current}</c>.</summary>
    public void PreRegisterLanguageTables(ITypeResolver typeResolver, string assemblyName)
    {
        foreach (var table in LanguageTables)
        {
            typeResolver.RegisterGeneratedType(new LanguageTableResolvedType(table, assemblyName));
        }
    }

    /// <summary>Registers a control document or theme variant as its generated type before any body is transformed, so
    /// documents embedding it resolve it regardless of file order; with <paramref name="anyClass"/>, every document that
    /// generates a class - a style set, a resource dictionary, a theme. No-op for other roots.</summary>
    public IResolvedType PreRegisterDocument(AumlDocument document, ITypeResolver typeResolver, bool anyClass = false)
    {
        typeResolver.ScanXmlnsAttributes();
        foreach (var mapping in document.NamespaceMappings)
        {
            if (!mapping.IsClrNamespace) continue;
            if (string.IsNullOrEmpty(mapping.Assembly))
                typeResolver.FindAssemblyByNamespace(mapping.Namespace);
            else
                typeResolver.ResolveAssembly(mapping.Assembly);
        }

        var rootRef = document.Root?.TypeReference;
        if (rootRef == null) return null;

        var resolvedRoot = ResolveRootReference(rootRef, typeResolver);
        if (resolvedRoot == null) return null;

        var rootType = typeResolver.Resolve(resolvedRoot.GetFullTypeName());
        if (rootType is not { EntityType: EntityType.Window or EntityType.View
                              or EntityType.UIApplication or EntityType.ThemeVariant or EntityType.Control
                              or EntityType.DataTemplateSet }
            && !(anyClass && rootType is { EntityType: not EntityType.Unknown }))
        {
            return null;
        }

        // Reuse the resolved root reference in the full Transform (which short-circuits on IsResolved) and as the
        // registered type's BaseType chain (MetadataResolvedType.BaseType reads RootNode's type reference).
        document.Root.TypeReference = resolvedRoot;

        var className = Path.GetFileNameWithoutExtension(document.RelativeFilePath);
        var container = new AumlMetadataContainer(typeResolver)
        {
            RelativeFilePath = document.RelativeFilePath,
            SourceFilePath = document.SourceFilePath,
            AssemblyName = document.RootNamespace,
            RootNode = document.Root,
            RootEntityType = rootType.EntityType,
            ClassName = className,
            Namespace = AumlNaming.ComputeNamespace(document.RelativeFilePath, document.RootNamespace, document.Root, className),
        };

        var resolvedType = new MetadataResolvedType(container);
        typeResolver.RegisterGeneratedType(resolvedType);
        return resolvedType;
    }

    // Resolves the document ROOT element's type reference (Window/View/...) to a concrete type. The root is always a
    // real framework type, so it resolves against the compilation even in the lightweight pre-registration pass.
    private IAumlAstTypeReference ResolveRootReference(IAumlAstTypeReference rootRef, ITypeResolver typeResolver)
    {
        if (rootRef.IsResolved) return rootRef;

        IResolvedType type;
        if (rootRef.IsXmlNamespaceDeclaration && !string.IsNullOrEmpty(rootRef.Namespace))
        {
            type = typeResolver.GetResolvedAssemblyByXmlDefinition(rootRef.Namespace)
                ?.GetTypeByShortName(rootRef.Name);
        }
        else if (string.IsNullOrEmpty(rootRef.Namespace))
        {
            type = typeResolver.ResolveByShortName(rootRef.Name);
        }
        else
        {
            type = typeResolver.FindAssemblyByNamespace(rootRef.Namespace)
                ?.GetTypeByFullName(rootRef.GetFullTypeName());
        }

        if (type == null) return null;

        return new AumlAstResolvedTypeReference(
            rootRef.GetLineInfo(),
            type.Namespace,
            type.Name,
            type.AssemblyName,
            type.InheritsFromMarkupExtension(AumlParser.MarupExtensionClassFullName));
    }
}