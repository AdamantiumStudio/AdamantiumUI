using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.Exceptions;
using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.Markup.CodeGeneration;

public class DefaultAumlTransformer : IAumlTransformer
{
    /// <summary>The language tables <c>{Localize}</c> can name besides the compiled ones: this project's, generated in the
    /// same build from its .alang files, so they are found and checked before they are in the compilation.</summary>
    public IReadOnlyCollection<LanguageTableShape> LanguageTables { get; set; } = [];

    private const string MarkupItemAttributeName = "Adamantium.UI.Core.MarkupItemAttribute";

    // {Localize Table, Key={Binding Kind}}: where a key known only at run time is read from.
    private const string KeyArgument = "Key";

    // {Localize Table={Binding Phrases}, Key={Binding Name}}: where a table known only at run time is read from.
    private const string TableArgument = "Table";

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
            var resolvedAssembly = typeResolver.GetResolvedAssembly(markupExtension.TypeReference.Assembly);

            if (resolvedAssembly == null)
            {
                diagnostics.ReportError(document.FileName,
                    $"Assembly {markupExtension.TypeReference.Assembly} could not be found. {markupExtension.GetLineInfo()}");
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
                        $"Property {argument.Name} could not be found in {markupExtension.TypeReference.GetFullTypeName()}. {markupExtension.GetLineInfo()}");
                }
                else
                {
                    CheckTypeOf(type.GetMemberByName(property.Name), transformedValue);
                }

                if (transformedValue is AumlAstMarkupExtensionLiteral literal)
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
                    diagnostics.ReportError(document.FileName, $"Xml namespace {typeReference.Namespace} could not be found. {lineInfo}");
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

                    diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in namespace {typeReference.Namespace}. {lineInfo}");
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
                    diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in namespace {typeReference.Namespace}. {lineInfo}");
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
                    diagnostics.ReportError(document.FileName, $"Xml namespace {typeReference.Namespace} could not be found. {lineInfo}");
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
                        diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in namespace {typeReference.Namespace}. {lineInfo}");
                        return typeReference;
                    }
                }

                var propertyInfo = typeInfo.GetAllProperties().FirstOrDefault(x=>x.Name == propertyReference.Name);

                if (propertyInfo == null)
                {
                    diagnostics.ReportError(document.FileName, $"Property {propertyReference.Name} could not be found in {typeReference.Name}. {lineInfo}");
                    return typeReference;
                }

                return CreateResolved(propertyInfo.PropertyType, lineInfo);
            }

            // not XmlNamespaceDeclaration
            if (string.IsNullOrEmpty(typeReference.Assembly) || string.IsNullOrEmpty(typeReference.Namespace))
            {
                return ResolveByNameOnly(typeReference, lineInfo);
            }

            // CLR type reference
            var clrTypeContainer = typeResolver.ResolveAssembly(typeReference.Assembly);
            if (clrTypeContainer != null)
            {
                var typeInfo = clrTypeContainer.GetTypeByShortName(typeReference.Name);
                if (typeInfo == null)
                {
                    diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in namespace {typeReference.Namespace}. {lineInfo}");
                    return typeReference;
                }
                return CreateResolved(typeInfo, lineInfo);
            }

            throw new TypeNotAvailableException($"Type {typeReference.Name} is not available");
        }

        IAumlAstTypeReference ResolveByFullTypeNameWithoutAssembly(IAumlAstTypeReference typeReference, IAumlLineInfo lineInfo)
        {
            var typeInfo = typeResolver.FindAssemblyByNamespace(typeReference.Namespace);

            if (typeInfo == null)
            {
                diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in any linked assembly. {lineInfo}");
                return typeReference;
            }
            
            var type = typeInfo.GetTypeByFullName(typeReference.GetFullTypeName());
            if (type == null)
            {
                diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in namespace {typeReference.Namespace}. {lineInfo}");
                return typeReference;
            }

            return CreateResolved(type, lineInfo);
        }

        IAumlAstTypeReference ResolveByNameOnly(IAumlAstTypeReference typeReference, IAumlLineInfo lineInfo)
        {
            var typeInfo = typeResolver.ResolveByShortName(typeReference.Name);

            if (typeInfo == null)
            {
                diagnostics.ReportError(document.FileName, $"Type {typeReference.Name} could not be found in any linked assembly. {lineInfo}");
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

        void CheckTypeOf(IResolvedMember member, IAumlAstValueNode value)
        {
            if (member == null || value is not AumlAstTypeReferenceValueNode { TypeReference.IsResolved: true } typeValue)
            {
                return;
            }

            var type = typeResolver.Resolve(typeValue.TypeReference.GetFullTypeName());
            if (member.TypeOfProblem(type) is { } problem)
            {
                diagnostics.ReportError(document.FileName, $"{problem} (line {value.Line}, position {value.Position}).");
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
                        ? $"Directive 'x:{directive.Name}' is written on the element, not in a value. {directive.GetLineInfo()}"
                        : $"Unknown directive 'x:{directive.Name}'. {directive.GetLineInfo()}");
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
                    $"{{Localize}} names a table and a string, {{Localize Strings.Close}}, or a table and where the key is read from, {{Localize Strings, Key={{Binding Kind}}}}; got '{text}'. {localize.GetLineInfo()}");
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
                diagnostics.ReportError(document.FileName, $"{table} has no string '{key}'. {localize.GetLineInfo()}");
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
                    $"{{Localize {table}, {KeyArgument}=...}} reads the key from a binding; a key written out is {{Localize {table}.Close}}. {localize.GetLineInfo()}");
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
                    $"{{Localize}} names its table, {{Localize {named}, ...}}, or reads it from a binding, {{Localize {TableArgument}={{Binding Phrases}}, ...}}; not both. {localize.GetLineInfo()}");
                return localize;
            }

            if (!IsFollowed(tableSource.Value) || keySource == null || !IsFollowed(keySource.Value))
            {
                diagnostics.ReportError(document.FileName,
                    $"{{Localize {TableArgument}=..., {KeyArgument}=...}} reads both the table and the key from a binding: {{Localize {TableArgument}={{Binding Phrases}}, {KeyArgument}={{Binding Name}}}}. {localize.GetLineInfo()}");
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
                $"No language table '{(prefix.Length > 0 ? prefix + ":" : string.Empty)}{table}': a table is a set of {table}.<language>.alang files, here or in a referenced assembly. {localize.GetLineInfo()}");

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

            var fills = placeholders.Count == 0 ? "it has no placeholders" : $"it fills {string.Join(", ", placeholders)}";
            var problem = unknown.Count > 0
                ? $"{table}.{key} has no placeholder {string.Join(", ", unknown.Select(n => $"'{n}'"))}: {fills}"
                : $"{table}.{key} needs {string.Join(", ", missing)}: {fills}";
            diagnostics.ReportError(document.FileName, $"{problem}. {localize.GetLineInfo()}");
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
                    $"x:Static expects 'Type.Member', got '{body}'. {directive.GetLineInfo()}");
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
                    $"x:Static type '{typeText}' could not be resolved. {directive.GetLineInfo()}");
                return directive;
            }

            var owner = typeResolver.Resolve(resolvedRef.GetFullTypeName());
            if (owner?.GetMemberByName(memberName) == null)
            {
                diagnostics.ReportError(document.FileName,
                    $"x:Static: '{resolvedRef.GetFullTypeName()}' has no member '{memberName}'. {directive.GetLineInfo()}");
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
                ? $"Directive 'x:{directive.Name}' belongs in a value, not on the element. {directive.GetLineInfo()}"
                : $"Unknown directive 'x:{directive.Name}'. {directive.GetLineInfo()}");
        }

        bool MissingValue(AumlAstDirective directive, string body)
        {
            if (!string.IsNullOrWhiteSpace(body)) return false;

            diagnostics.ReportError(document.FileName, $"Directive '{directive.Name}' is missing a value. {directive.GetLineInfo()}");
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
                    $"[MarkupItem] on {reference.TargetType.GetFullTypeName()} names no reachable item property. {propertyNode.GetLineInfo()}");
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


        var entityType = EntityType.Unknown;
        var usings = new Dictionary<string, string>();

        var node = document.Root;
        TransformTypeForElement(node);
        container.RootClassName = container.FileName;
                
        var rootType = typeResolver.Resolve(node.TypeReference.GetFullTypeName());
        if (rootType == null)
        {
            diagnostics.ReportError(document.FileName,
                $"{node.TypeReference.GetFullTypeName()} could not be found. Please, check correctness of namespace");
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
                    foreach (var child in objectNode.Children)
                    {
                        queue.Enqueue(child);
                    }
                    break;
                case AumlAstPropertyNode propertyNode:

                    ExpandShorthandCollection(propertyNode);

                    var reference = propertyNode.Property as AumlAstPropertyReference;
                    var takesType = reference is { IsAttachedProperty: false, TargetType.IsResolved: true }
                                    && reference.TargetType.GetFullTypeName() == "System.Type";
                    for (int i = 0; i < propertyNode.Values.Count; i++)
                    {
                        var transformedValue = ProcessValueNode(ReadTypeName(propertyNode.Values[i], takesType));
                        propertyNode.Values[i] = transformedValue;
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
                                $"x:KeepAlive expects {string.Join(", ", modes)}, got '{mode}'. {directive.GetLineInfo()}");
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
                                    $"x:Load takes True, False or a binding, got '{condition}'. {directive.GetLineInfo()}");
                            }
                        }
                        else if (condition == "True")
                        {
                            // Legal, and does nothing: the element is built either way. Worth saying, because a
                            // directive that reads as "I arranged something here" and arranges nothing is exactly what
                            // nobody notices - and it still costs a slot and turns the name into an accessor.
                            diagnostics.ReportWarning(document.FileName,
                                $"x:Load=\"True\" holds nothing back - the element is built anyway. Remove it, or give " +
                                $"it a condition to answer. {directive.GetLineInfo()}");
                        }
                        else if (condition != "False")
                        {
                            diagnostics.ReportError(document.FileName,
                                $"x:Load takes True, False or a binding, got '{condition}'. {directive.GetLineInfo()}");
                        }
                    }
                    else if (directive.Name == AumlDirectives.DataType)
                    {
                        // Declared, not inferred: the type is what tooling resolves {Binding} paths against inside the
                        // template. Nothing is generated from it - what IS checked here is that the name resolves, so a
                        // renamed model does not leave a template silently pointing at nothing.
                        if (directive.Value is AumlAstTextNode dataTypeNode && !string.IsNullOrWhiteSpace(dataTypeNode.Text))
                        {
                            var dataTypeRef = MarkupExtensionParser.ParseTypeName(new ParserContext(null),
                                UnwrapTypeText(dataTypeNode.Text), directive.GetLineInfo(), document.NamespaceMappings.ToList());
                            if (ProcessTypeReference(dataTypeRef, directive.GetLineInfo()) is not { IsResolved: true })
                            {
                                diagnostics.ReportError(document.FileName,
                                    $"x:DataType '{dataTypeNode.Text}' could not be resolved. {directive.GetLineInfo()}");
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
                                diagnostics.ReportError(document.FileName, $"x:ViewModel type '{vmNode.Text}' could not be resolved. {directive.GetLineInfo()}");
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
                    $"TargetName='{target.Text}' points at an element held back by x:Load. It would resolve to nothing " +
                    $"until that element is built, and silently do nothing until then. {target.GetLineInfo()}");
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
                              or EntityType.UIApplication or EntityType.ThemeVariant or EntityType.Control }
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