using System.Text.RegularExpressions;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.Localization;
using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What a document's tree has to keep to that its types alone do not say: elements markup can build, nothing
/// set twice, one value where one fits and of a type that fits, children and text only where they have a place, names
/// unique in their scope - a template is one of its own - and keys unique in their dictionary.</summary>
internal sealed class MarkupStructureCheck
{
    private const string ContentAttribute = "Adamantium.UI.Core.ContentAttribute";
    private const string UiTemplate = "Adamantium.UI.Core.Templates.UiTemplate";
    private const string BindingBase = "Adamantium.UI.Core.Data.BindingBase";

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];

    private readonly ITypeResolver _resolver;
    private readonly IDiagnosticSink _diagnostics;
    private readonly string _file;
    private readonly IResolvedType _viewModel;

    public MarkupStructureCheck(ITypeResolver resolver, IDiagnosticSink diagnostics, string file, string viewModel = null)
    {
        _resolver = resolver;
        _diagnostics = diagnostics;
        _file = file;
        _viewModel = string.IsNullOrEmpty(viewModel) ? null : resolver.Resolve(viewModel);
    }

    public void Run(AumlAstObjectNode root) => Element(root, true, [], _viewModel);

    private void Element(AumlAstObjectNode node, bool isRoot, HashSet<string> names, IResolvedType context)
    {
        var type = TypeOf(node);
        if (type != null && !isRoot && !type.IsCreatable)
        {
            Report(MarkupMessages.NotCreatable(type.Name), node);
        }

        Name(node, names);
        var isTemplate = type != null && type.InheritsFrom(UiTemplate);
        HashSet<string> scope = isTemplate ? [] : names;

        var own = !isRoot && (type == null || type is MetadataResolvedType) ? null : context;
        var dataContext = node.Children.OfType<AumlAstPropertyNode>()
            .FirstOrDefault(p => p.Property is AumlAstPropertyReference { IsAttachedProperty: false, Name: "DataContext" });
        if (dataContext != null)
        {
            own = DataContextOf(dataContext, context);
        }

        var inner = isTemplate ? DataTypeOf(node)
            : type != null && type.Name is "Style" or "Setter" ? null
            : own;

        var set = new Dictionary<string, AumlAstPropertyNode>(StringComparer.Ordinal);
        foreach (var property in node.Children.OfType<AumlAstPropertyNode>())
        {
            if (property.Property is not AumlAstPropertyReference reference)
            {
                continue;
            }

            var name = reference.IsAttachedProperty ? $"{reference.OwnerType?.Name}.{reference.Name}" : reference.Name;
            if (set.ContainsKey(name))
            {
                Report(MarkupMessages.PropertySetTwice(name, node.TypeReference?.Name), reference);
            }
            else
            {
                set[name] = property;
            }

            Property(type, reference, property, isRoot);
            var bound = property == dataContext ? context : isTemplate ? null : inner;
            Bindings(property.Values, bound);
            var values = property.Values.OfType<AumlAstObjectNode>().ToList();
            var isResources = reference.Name == "Resources";
            if (isResources)
            {
                Keys(values);
            }

            foreach (var value in values)
            {
                Element(value, false, scope, isResources ? null : bound);
            }
        }

        Children(node, type, set);
        var children = node.GetLogicalChildrenObjects().ToList();
        Triggered(node, type, isTemplate, set, children);
        if (type != null && type.ImplementsInterface("IResourceDictionary"))
        {
            Keys(children);
        }

        foreach (var child in children)
        {
            Element(child, false, scope, inner);
        }
    }

    private void Property(IResolvedType type, AumlAstPropertyReference reference, AumlAstPropertyNode property, bool isRoot)
    {
        if (type == null || property.Values.Count == 0)
        {
            return;
        }

        var member = reference.IsAttachedProperty
            ? (_resolver.Resolve(reference.OwnerType.GetFullTypeName()))?.GetMemberByName("Get" + reference.Name)
            : type.GetMemberByName(reference.Name);
        if (member?.MemberType is not { } propertyType || member.MemberKind == ResolvedMemberKind.Event)
        {
            return;
        }

        var many = PropertyValues.TakesMany(propertyType);
        if (!isRoot && !reference.IsAttachedProperty && member.MemberKind == ResolvedMemberKind.Property && !member.HasSetter() && !many &&
            !property.Values.Any(IsExtension))
        {
            Report(MarkupMessages.ReadOnlyProperty(type.Name, reference.Name), reference);
            return;
        }

        var values = property.Values.OfType<AumlAstObjectNode>().ToList();
        if (many || values.Count == 0)
        {
            return;
        }

        if (values.Count > 1)
        {
            Report(MarkupMessages.OneValue(reference.Name), values[1]);
            return;
        }

        if (property.Values.Count == 1 && TypeOf(values[0]) is { } valueType && !PropertyValues.Fits(valueType, propertyType))
        {
            Report(MarkupMessages.WrongValueType(valueType.Name, propertyType.Name, reference.Name), values[0]);
        }
    }

    private void Children(AumlAstObjectNode node, IResolvedType type, Dictionary<string, AumlAstPropertyNode> set)
    {
        if (type == null)
        {
            return;
        }

        if (type.TypeKind is not (ResolvedTypeKind.Struct or ResolvedTypeKind.Enum) && type.SpecialType != ResolvedSpecialType.System_String &&
            node.Children.OfType<AumlAstTextNode>().FirstOrDefault(text => !string.IsNullOrWhiteSpace(text.Text)) is { } stray)
        {
            Report(MarkupMessages.NoPlaceForText(type.Name), stray);
        }

        var children = node.GetLogicalChildrenObjects().Where(child => CollectionFor(type, child) == null).ToList();
        if (children.Count == 0 || type.Name == "Style" || type.ImplementsInterface("ITrigger") || type.InheritsFrom(BindingBase))
        {
            return;
        }

        if (type.InheritsFrom(UiTemplate))
        {
            if (children.Count > 1)
            {
                Report(MarkupMessages.TemplateTakesOne(type.Name), children[1]);
            }

            return;
        }

        if (type.FindPropertyWithAttribute(ContentAttribute, out var content))
        {
            if (content.PropertyType == null || content.PropertyType.IsCollection())
            {
                return;
            }

            if (set.ContainsKey(content.Name))
            {
                Report(MarkupMessages.ContentSetTwice(type.Name, content.Name), children[0]);
            }
            else if (children.Count > 1)
            {
                Report(MarkupMessages.ContentTakesOne(type.Name, content.Name), children[1]);
            }

            return;
        }

        if (type.ImplementsInterface("IContainer") || type.ImplementsInterface("IResourceContainer") || type.IsCollection())
        {
            return;
        }

        Report(MarkupMessages.NoPlaceForChild(type.Name), children[0]);
    }

    private IResolvedType DataContextOf(AumlAstPropertyNode dataContext, IResolvedType context)
    {
        if (dataContext.Values.Count != 1)
        {
            return null;
        }

        return dataContext.Values[0] switch
        {
            AumlAstMarkupExtensionNode { TypeReference.Name: "Binding" } binding when PathOf(binding) is { } path => Read(path, context, null),
            AumlAstObjectNode element => TypeOf(element),
            _ => null
        };
    }

    private IResolvedType DataTypeOf(AumlAstObjectNode template) =>
        template.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.DataType)?.Value is
            AumlAstTypeReferenceValueNode { TypeReference: { } reference }
            ? _resolver.Resolve(reference.GetFullTypeName())
            : null;

    private void Bindings(IEnumerable<IAumlAstValueNode> values, IResolvedType context)
    {
        if (context == null)
        {
            return;
        }

        foreach (var value in values)
        {
            switch (value)
            {
                case AumlAstMarkupExtensionNode { TypeReference.Name: "Binding" } binding:
                    if (PathOf(binding) is { } path)
                    {
                        Read(path, context, binding);
                    }

                    break;
                case AumlAstMarkupExtensionNode extension:
                    Bindings(extension.Arguments.Select(argument => argument.Value), context);
                    break;
                case AumlAstObjectNode { TypeReference.Name: "MultiBinding" } multi:
                    Bindings(multi.GetLogicalChildrenObjects(), context);
                    break;
                case AumlAstObjectNode { TypeReference.Name: "Binding" } element:
                    var set = element.Children.OfType<AumlAstPropertyNode>()
                        .Where(p => p.Property is AumlAstPropertyReference)
                        .GroupBy(p => ((AumlAstPropertyReference)p.Property).Name)
                        .ToDictionary(g => g.Key, g => g.First());
                    if (!set.ContainsKey("Source") && !set.ContainsKey("ElementName") && !set.ContainsKey("RelativeSource") &&
                        TextOf(set, "Path") is { } elementPath)
                    {
                        Read(elementPath, context, (AumlAstPropertyReference)set["Path"].Property);
                    }

                    break;
            }
        }
    }

    private static string PathOf(AumlAstMarkupExtensionNode binding)
    {
        string path = null;
        foreach (var argument in binding.Arguments)
        {
            if (argument.Name is "Source" or "ElementName" or "RelativeSource")
            {
                return null;
            }

            if (string.IsNullOrEmpty(argument.Name) || argument.Name == "Path")
            {
                path = argument.Value?.GetTextValue()?.Trim();
            }
        }

        return path;
    }

    private IResolvedType Read(string path, IResolvedType context, IAumlLineInfo at)
    {
        var type = context;
        if (path.Length == 0 || path == ".")
        {
            return type;
        }

        foreach (var step in path.Split('.'))
        {
            if (type == null || step.Length == 0 || step.IndexOfAny(['[', '(', ')', ']']) >= 0 ||
                type.SpecialType == ResolvedSpecialType.System_Object || type.TypeKind == ResolvedTypeKind.Interface ||
                type.IsGenericType || type is MetadataResolvedType)
            {
                return null;
            }

            if (!ViewModelMembers.TryFind(type, step, out var next))
            {
                if (at != null)
                {
                    Report(MarkupMessages.BindingPathNotFound(type.Name, step, path), at);
                }

                return null;
            }

            type = next;
        }

        return type;
    }

    private void Triggered(AumlAstObjectNode node, IResolvedType type, bool isTemplate,
        Dictionary<string, AumlAstPropertyNode> set, List<AumlAstObjectNode> children)
    {
        if (type?.Name == "Style")
        {
            if (TextOf(set, "Selector") is { Length: > 0 } selector && ResolveShort(selector.Split('.', '#')[0]) is { } target)
            {
                var template = children.Where(IsSetter)
                    .Where(setter => TextOf(Properties(setter), "Property") == "Template")
                    .SelectMany(setter => Properties(setter).TryGetValue("Value", out var value) ? value.Values : [])
                    .OfType<AumlAstObjectNode>()
                    .FirstOrDefault();
                var parts = template == null ? null : PartsOf(template);
                Setters(children.Where(IsSetter), target, parts, template?.TypeReference?.Name);
                Triggers(children.Where(IsTrigger), target, parts, template?.TypeReference?.Name);
            }

            return;
        }

        if (!set.TryGetValue("Triggers", out var triggers) || type == null)
        {
            return;
        }

        if (isTemplate)
        {
            if (TargetTypeOf(set) is { } target)
            {
                Triggers(triggers.Values.OfType<AumlAstObjectNode>(), target, PartsOf(node), type.Name);
            }
        }
        else
        {
            Triggers(triggers.Values.OfType<AumlAstObjectNode>(), type, null, null);
        }
    }

    private void Triggers(IEnumerable<AumlAstObjectNode> triggers, IResolvedType target,
        Dictionary<string, IResolvedType> parts, string template)
    {
        foreach (var trigger in triggers)
        {
            var set = Properties(trigger);
            switch (trigger.TypeReference?.Name)
            {
                case "PropertyTrigger":
                    if (Part(set, "SourceName", target, parts, template, out var source))
                    {
                        Member(source, set);
                    }

                    break;
                case "MultiTrigger":
                    if (set.TryGetValue("Conditions", out var conditions))
                    {
                        foreach (var condition in conditions.Values.OfType<AumlAstObjectNode>())
                        {
                            var conditionSet = Properties(condition);
                            if (Part(conditionSet, "SourceName", target, parts, template, out var conditionSource))
                            {
                                Member(conditionSource, conditionSet);
                            }
                        }
                    }

                    break;
            }

            Setters(trigger.GetLogicalChildrenObjects().Where(IsSetter), target, parts, template);
        }
    }

    private void Setters(IEnumerable<AumlAstObjectNode> setters, IResolvedType target, Dictionary<string, IResolvedType> parts,
        string template)
    {
        foreach (var setter in setters)
        {
            var set = Properties(setter);
            if (Part(set, "TargetName", target, parts, template, out var on))
            {
                Member(on, set);
            }
        }
    }

    private bool Part(Dictionary<string, AumlAstPropertyNode> set, string key, IResolvedType target,
        Dictionary<string, IResolvedType> parts, string template, out IResolvedType on)
    {
        on = target;
        if (TextOf(set, key) is not { Length: > 0 } name)
        {
            return true;
        }

        if (parts == null)
        {
            return false;
        }

        if (!parts.TryGetValue(name, out on))
        {
            Report(MarkupMessages.PartNotFound(template, name), set[key].Property);
            return false;
        }

        return on != null;
    }

    private void Member(IResolvedType on, Dictionary<string, AumlAstPropertyNode> set)
    {
        if (TextOf(set, "Property") is not { Length: > 0 } name)
        {
            return;
        }

        var at = set["Property"].Property;
        var dot = name.IndexOf('.');
        var member = dot < 0
            ? on.GetMemberByName(name)
            : ResolveShort(name.Substring(0, dot))?.GetMemberByName("Get" + name.Substring(dot + 1));
        if (member?.MemberType is not { } propertyType)
        {
            Report(MarkupMessages.PropertyNotFoundIn(name, dot < 0 ? on.Name : name.Substring(0, dot)), at);
            return;
        }

        if (set.TryGetValue("Value", out var value) && value.Values.Count == 1 && value.Values[0].IsTextNode() &&
            value.GetTextValue()?.Trim() is { } text &&
            (DefaultAumlTransformer.ExpectedLiteral(propertyType, text) ?? MarkupValueChecks.Problem(propertyType.FullName, text)) is { } expected)
        {
            var valueAt = value.Property;
            Report(MarkupMessages.InvalidLiteral(text, propertyType.Name, name, expected, valueAt.Line, valueAt.Position), valueAt);
        }
    }

    private Dictionary<string, IResolvedType> PartsOf(AumlAstObjectNode template)
    {
        var parts = new Dictionary<string, IResolvedType>(StringComparer.Ordinal);
        Collect(template);
        return parts;

        void Collect(AumlAstObjectNode node)
        {
            foreach (var child in node.Children)
            {
                switch (child)
                {
                    case AumlAstDirective { Name: AumlDirectives.Name, Value: AumlAstTextNode { Text: var name } }:
                        parts[name.Trim()] = TypeOf(node);
                        break;
                    case AumlAstPropertyNode property:
                        foreach (var value in property.Values.OfType<AumlAstObjectNode>().Where(value => !IsTemplate(value)))
                        {
                            Collect(value);
                        }

                        break;
                    case AumlAstObjectNode element when !IsTemplate(element):
                        Collect(element);
                        break;
                }
            }
        }
    }

    private IResolvedType TargetTypeOf(Dictionary<string, AumlAstPropertyNode> set)
    {
        if (!set.TryGetValue("TargetType", out var targetType) || targetType.Values.Count != 1)
        {
            return null;
        }

        return targetType.Values[0] switch
        {
            AumlAstTypeReferenceValueNode { TypeReference: { } reference } => _resolver.Resolve(reference.GetFullTypeName()),
            var value when value.IsTextNode() && value.GetTextValue()?.Trim() is { Length: > 0 } text => ResolveShort(text),
            _ => null
        };
    }

    private bool IsTemplate(AumlAstObjectNode node) => TypeOf(node)?.InheritsFrom(UiTemplate) == true;

    private static bool IsSetter(AumlAstObjectNode node) => node.TypeReference?.Name == "Setter";

    private bool IsTrigger(AumlAstObjectNode node) => TypeOf(node)?.ImplementsInterface("ITrigger") == true;

    private static Dictionary<string, AumlAstPropertyNode> Properties(AumlAstObjectNode node) => node.Children
        .OfType<AumlAstPropertyNode>()
        .Where(p => p.Property is AumlAstPropertyReference)
        .GroupBy(p => ((AumlAstPropertyReference)p.Property).Name)
        .ToDictionary(g => g.Key, g => g.First());

    private IResolvedType ResolveShort(string name)
    {
        var colon = name.IndexOf(':');
        return _resolver.ResolveByShortName(colon < 0 ? name : name.Substring(colon + 1));
    }

    private static string TextOf(Dictionary<string, AumlAstPropertyNode> set, string property) =>
        set.TryGetValue(property, out var node) && node.Values.Count == 1 && node.Values[0].IsTextNode() ? node.GetTextValue()?.Trim() : null;

    private void Name(AumlAstObjectNode node, HashSet<string> names)
    {
        var directive = node.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.Name);
        if ((directive?.Value as AumlAstTextNode)?.Text?.Trim() is not { Length: > 0 } name)
        {
            return;
        }

        if (!Identifier.IsMatch(name) || Keywords.Contains(name))
        {
            Report(MarkupMessages.NameNotIdentifier(name), directive);
        }
        else if (!names.Add(name))
        {
            Report(MarkupMessages.NameTwice(name), directive);
        }
    }

    private void Keys(IEnumerable<AumlAstObjectNode> entries)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var directive = entry.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.Key);
            if ((directive?.Value as AumlAstTextNode)?.Text is { Length: > 0 } key && !keys.Add(key))
            {
                Report(MarkupMessages.KeyTwice(key), directive);
            }
        }
    }

    private string CollectionFor(IResolvedType parent, AumlAstObjectNode child)
    {
        var childType = TypeOf(child);
        if (childType == null || childType.ImplementsInterface("IUIComponent"))
        {
            return null;
        }

        foreach (var property in parent.GetAllProperties())
        {
            if (property.PropertyType?.GetAttribute(PropertyValues.MarkupItemAttribute) is { } item &&
                item.NamedArguments.TryGetValue("ItemType", out var itemType) && itemType != null &&
                childType.IsAssignableTo(itemType.ToString()))
            {
                return property.Name;
            }
        }

        return null;
    }

    private bool IsExtension(IAumlAstValueNode value) => value is IAumlAstMarkupExtensionNode ||
        (value is AumlAstObjectNode element && TypeOf(element)?.InheritsFromMarkupExtension(AumlParser.MarupExtensionClassFullName) == true);

    private IResolvedType TypeOf(AumlAstObjectNode node) =>
        node.TypeReference is { } reference ? _resolver.Resolve(reference.GetFullTypeName()) : null;

    private void Report(string message, IAumlLineInfo at) => _diagnostics.ReportError(_file, message, at);
}
