using System.Text.RegularExpressions;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.LanguageServer;

public enum AumlCompletionItemKind { Element, Property, Value, Directive }

/// <param name="InsertText">Optional snippet to insert instead of <paramref name="Label"/> (LSP snippet syntax,
/// e.g. <c>Name="$0"</c> to drop the caret between the quotes). Null = insert the label verbatim.</param>
/// <param name="ReplaceBack">When set, the item replaces this many characters immediately before the caret (an
/// explicit edit range) instead of letting the client guess the word boundary. Needed for path segments: after
/// <c>Textures/</c> the client would otherwise filter bare file names against the whole "Textures/" prefix and
/// hide them — here only the segment after the last '/' is the prefix/replacement.</param>
/// <param name="Color">The color the item stands for, "#RRGGBB" or "#AARRGGBB"; null when it is not one.</param>
public sealed record AumlCompletionItem(string Label, AumlCompletionItemKind Kind, string Detail = null, string InsertText = null,
    int? ReplaceBack = null, string Color = null);

/// <summary>
/// Produces AUML completions at a caret position: element names, an element's settable
/// properties (plus the <c>x:</c> directives), or an attribute's value set (enum members /
/// booleans). Resolves xmlns prefixes from the buffer's declarations.
/// </summary>
public sealed class CompletionEngine
{
    private const string FallbackXmlns = "http://adamantium/ui";
    private const string LocalizeExtension = "Localize";

    private readonly AumlTypeModel _model;

    public CompletionEngine(AumlTypeModel model) => _model = model;

    public IReadOnlyList<AumlCompletionItem> Complete(string text, int offset, string documentPath = null)
    {
        var ctx = AumlCaretContext.Detect(text, offset);
        var namespaces = AumlNamespaces.Scan(text);
        return ctx.Kind switch
        {
            AumlCompletionKind.ElementName => CompleteElements(ctx, namespaces, text, offset),
            AumlCompletionKind.AttributeName => CompleteAttributes(ctx, namespaces, text, offset),
            AumlCompletionKind.AttributeValue => CompleteValues(ctx, namespaces, text, offset, documentPath),
            AumlCompletionKind.MarkupExtensionName => CompleteMarkupExtensionName(ctx, namespaces, text, offset),
            AumlCompletionKind.MarkupExtensionArg => CompleteMarkupExtensionArg(ctx, namespaces, text, offset),
            _ => []
        };
    }

    // After '{': offer the available markup-extension names (TemplateBinding, ResourceReference, …). ReplaceBack only
    // covers the partial name typed after '{' (so the '{' is preserved, not eaten by the client's word guess), and the
    // snippet closes the brace + drops the caret where the argument goes: "{Binding |}".
    private IReadOnlyList<AumlCompletionItem> CompleteMarkupExtensionName(
        AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces, string text, int offset)
    {
        // Don't append a closing brace when the editor already auto-paired one right after the caret ("{Th|}") - that
        // extra '}' is exactly what produced the annoying "{ThemeResource }}".
        var closeBrace = offset < text.Length && text[offset] == '}' ? "" : "}";
        var items = _model.GetMarkupExtensions()
            .Where(n => Matches(n, ctx.Prefix))
            .Select(n => new AumlCompletionItem(n, AumlCompletionItemKind.Element, InsertText: n + " $0" + closeBrace, ReplaceBack: ctx.Prefix.Length))
            .ToList();

        var xPrefix = namespaces.FirstOrDefault(n => n.Value == AumlXDirectives.Xmlns).Key;
        if (xPrefix == null)
        {
            return items;
        }

        foreach (var directive in AumlDirectives.All.Where(d => d.Usage == AumlDirectiveUsage.Value))
        {
            var name = $"{xPrefix}:{directive.Name}";
            if (!Matches(name, ctx.Prefix))
            {
                continue;
            }

            var insert = directive.Name == AumlDirectives.Null ? name + closeBrace : name + " $0" + closeBrace;
            items.Add(new AumlCompletionItem(name, AumlCompletionItemKind.Directive, directive.Description,
                InsertText: insert, ReplaceBack: ctx.Prefix.Length));
        }

        return items;
    }

    // Completes arguments of any "{Name arg}" extension: named arguments from its properties, values by type; extensions
    // with special sources (TemplateBinding, x:Type, resources) by name.
    private IReadOnlyList<AumlCompletionItem> CompleteMarkupExtensionArg(
        AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces, string text, int offset)
    {
        var open = OpenExtensions(text, offset);
        if (open.Count == 0)
        {
            return [];
        }

        var extLocal = LocalName(open[^1].Name);
        if (extLocal == LocalizeExtension)
        {
            return CompleteLocalize(text, offset, namespaces);
        }

        var extType = _model.ResolveMarkupExtensionType(extLocal);

        var (segment, hasComma) = LastArgument(open[^1].Arguments);
        int eq = segment.IndexOf('=');

        if (extLocal is "ObservableResource" or "ObservableResourceExtension" or "ResourceReference" or "ResourceReferenceExtension")
        {
            string keyPartial = null;
            if (eq >= 0 && segment[..eq].Trim() == "Key")
            {
                keyPartial = segment[(eq + 1)..].TrimStart();
            }
            else if (eq < 0 && !hasComma)
            {
                keyPartial = segment.Trim();
            }

            if (keyPartial != null)
            {
                return CompleteResourceKeys(keyPartial, TargetPropertyType(open, ctx, namespaces));
            }
        }

        // "Name=value" -> complete the value of that named property by its type.
        if (eq >= 0)
        {
            var propName = segment[..eq].Trim();
            var partial = segment[(eq + 1)..].TrimStart();
            var propType = extType is null ? null : _model.GetPropertyType(extType, propName);
            return CompleteExtensionValue(extLocal, propType, partial, text, offset, namespaces,
                TypeBase(extLocal, extType, propName, open, ctx, namespaces));
        }

        var seg = segment.Trim();
        var defaultProp = extType is null ? null : _model.GetDefaultProperty(extType);

        // First (positional) segment: complete the extension's default-argument value (its [DefaultProperty], or a
        // name-dispatched source for extensions whose positional value isn't a CLR property of their own).
        if (!hasComma && (defaultProp is not null || IsPositionalValueExtension(extLocal)))
            return CompleteExtensionValue(extLocal, defaultProp?.PropertyType, seg, text, offset, namespaces,
                TypeBase(extLocal, extType, defaultProp?.Name, open, ctx, namespaces));

        // After a comma (or an extension with no positional arg) -> the extension's settable property NAMES.
        if (extType is null) return [];
        return _model.GetProperties(extType)
            .Where(p => Matches(p.Name, seg))
            .OrderBy(p => p.Name)
            .Select(p => new AumlCompletionItem(p.Name, AumlCompletionItemKind.Property, p.Type?.Name,
                InsertText: p.Name + "=", ReplaceBack: seg.Length))
            .ToList();
    }

    private string TypeBase(string extLocal, Adamantium.UI.Markup.CodeGeneration.IResolvedType extType, string propName,
        List<(string Name, string Arguments)> open, AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces)
    {
        if (extLocal is not ("Type" or "TypeExtension"))
        {
            return propName == null ? null : extType?.GetMemberByName(propName).TypeOfBase();
        }

        if (open.Count == 1)
        {
            return ResolveElement(ctx.ElementName, namespaces)?.GetMemberByName(ctx.AttributeName ?? "").TypeOfBase();
        }

        var outerType = _model.ResolveMarkupExtensionType(LocalName(open[^2].Name));
        if (outerType == null)
        {
            return null;
        }

        var (argument, _) = LastArgument(open[^2].Arguments);
        var eq = argument.IndexOf('=');
        var name = eq >= 0 ? argument[..eq].Trim() : _model.GetDefaultProperty(outerType)?.Name;
        return name == null ? null : outerType.GetMemberByName(name).TypeOfBase();
    }

    private IReadOnlyList<AumlCompletionItem> CompleteResourceKeys(string partial, IResolvedType target) =>
        _model.ResourceKeys
            .Where(k => Matches(k.Key, partial) && Holds(k, target))
            .OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => new AumlCompletionItem(k.Key, AumlCompletionItemKind.Value, k.ValueType?.Name, ReplaceBack: partial.Length))
            .ToList();

    private static bool Holds(AumlResourceKey key, IResolvedType target) =>
        target == null || target.FullName == "object" || target.FullName == "System.Object" || key.ValueType == null
        || key.ValueType.IsAssignableTo(target.FullName) || key.ValueType.ImplementsInterface(target.Name);

    private IResolvedType TargetPropertyType(List<(string Name, string Arguments)> open, AumlCompletionContext ctx,
        IReadOnlyDictionary<string, string> namespaces)
    {
        if (open.Count > 1)
        {
            var outerType = _model.ResolveMarkupExtensionType(LocalName(open[^2].Name));
            if (outerType == null)
            {
                return null;
            }

            var (argument, _) = LastArgument(open[^2].Arguments);
            var eq = argument.IndexOf('=');
            var name = eq >= 0 ? argument[..eq].Trim() : _model.GetDefaultProperty(outerType)?.Name;
            return name == null ? null : _model.GetPropertyType(outerType, name);
        }

        var attribute = ctx.AttributeName ?? string.Empty;
        var dot = attribute.IndexOf('.');
        if (dot > 0)
        {
            var (prefix, ownerName) = SplitName(attribute[..dot]);
            var owner = ResolveType(prefix, ownerName, namespaces);
            return owner == null ? null : _model.GetAttachedProperties(owner).FirstOrDefault(p => p.Name == attribute[(dot + 1)..])?.Type;
        }

        var element = ResolveElement(ctx.ElementName, namespaces);
        return element == null ? null : _model.GetPropertyType(element, attribute);
    }

    private static List<(string Name, string Arguments)> OpenExtensions(string text, int offset)
    {
        var end = Math.Min(offset, text.Length);
        var value = text[(text.LastIndexOf('"', Math.Max(0, end - 1)) + 1)..end];
        var starts = new List<int>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '{')
            {
                starts.Add(i);
            }
            else if (value[i] == '}' && starts.Count > 0)
            {
                starts.RemoveAt(starts.Count - 1);
            }
        }

        var open = new List<(string Name, string Arguments)>();
        for (var level = 0; level < starts.Count; level++)
        {
            var body = value[(starts[level] + 1)..(level + 1 < starts.Count ? starts[level + 1] : value.Length)];
            var space = body.IndexOf(' ');
            open.Add(space < 0 ? (body, "") : (body[..space], body[(space + 1)..]));
        }

        return open;
    }

    private static (string Segment, bool HasComma) LastArgument(string arguments)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < arguments.Length; i++)
        {
            depth += arguments[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (arguments[i] == ',' && depth == 0)
            {
                start = i + 1;
            }
        }

        return (arguments[start..], start > 0);
    }

    private static string LocalName(string name) => name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;

    // Extensions whose positional argument isn't a CLR property on the extension (so no [DefaultProperty]) but which
    // still complete a positional value, dispatched by name in CompleteExtensionValue.
    private static bool IsPositionalValueExtension(string extLocal) =>
        extLocal is "TemplateBinding" or "TemplateBindingExtension" or "Type" or "TypeExtension"
            or "ResourceReference" or "ResourceReferenceExtension"
            or "ThemeResource" or "ThemeResourceExtension" or AumlDirectives.Static;

    /// <summary>Completes the VALUE of a markup-extension argument from the appropriate external source. Special
    /// extensions are dispatched by name (TemplateBinding -> the ControlTemplate TargetType's properties; x:Type ->
    /// type names; resources -> resource keys); everything else is driven by the property's CLR type: a view-model path
    /// for a PropertyPath, type names for a Type, enum members / booleans / brush colors via the type model.</summary>
    private IReadOnlyList<AumlCompletionItem> CompleteExtensionValue(
        string extLocal, Adamantium.UI.Markup.CodeGeneration.IResolvedType propType,
        string partial, string text, int offset, IReadOnlyDictionary<string, string> namespaces, string typeBase)
    {
        if (extLocal is "TemplateBinding" or "TemplateBindingExtension")
        {
            var target = FindNearestAttributeType(text, offset, "TargetType", namespaces);
            if (target is null) return [];
            return _model.GetProperties(target)
                .Where(p => Matches(p.Name, partial))
                .OrderBy(p => p.Name)
                .Select(p => new AumlCompletionItem(p.Name, AumlCompletionItemKind.Property, p.Type?.Name, ReplaceBack: partial.Length))
                .ToList();
        }

        if (extLocal is "Type" or "TypeExtension")
            return CompleteTypeNames(partial, namespaces, typeBase);

        if (extLocal is AumlDirectives.Static)
            return CompleteStaticMembers(partial, namespaces);

        // {ThemeResource Key} -> only the theme's OWN brush keys (the Theme class's Brush properties), not the generic
        // color list the Brush-typed default property would otherwise pull in (which was confusing).
        if (extLocal is "ThemeResource" or "ThemeResourceExtension")
            return _model.GetThemeBrushKeys()
                .Where(k => Matches(k, partial))
                .Select(k => new AumlCompletionItem(k, AumlCompletionItemKind.Value, "Brush", ReplaceBack: partial.Length))
                .ToList();

        if (propType is null) return [];

        // PropertyPath value (e.g. Binding.Path) -> view-model bindable path completion.
        if (propType.Name == "PropertyPath")
            // The nearest declared data type wins: inside a DataTemplate that states x:DataType the paths belong to the
            // ITEM, not to the view's x:ViewModel - and "nearest" is what tells the two apart.
            return CompleteBindingPath(partial,
                FindNearestAttributeType(text, offset, "DataType", namespaces)
                ?? FindNearestAttributeType(text, offset, "ViewModel", namespaces));

        // A System.Type-valued property -> type names.
        if (propType.Name == "Type")
            return CompleteTypeNames(partial, namespaces, typeBase);

        // Enum members / booleans / brush colors handled uniformly by the type model.
        var values = _model.GetValueCompletions(propType);
        if (values.Count == 0) return [];
        return values
            .Where(v => Matches(v, partial))
            .Select(v => new AumlCompletionItem(v, AumlCompletionItemKind.Value, propType.Name, ReplaceBack: partial.Length,
                Color: ColorOf(propType, v)))
            .ToList();
    }

    private static string ColorOf(Adamantium.UI.Markup.CodeGeneration.IResolvedType type, string value) =>
        AumlColors.TakesColor(type) && AumlColors.TryRead(value, out var color) ? AumlColors.Hex(color) : null;

    private IReadOnlyList<AumlCompletionItem> CompleteStaticMembers(string partial, IReadOnlyDictionary<string, string> namespaces)
    {
        var dot = partial.LastIndexOf('.');
        if (dot < 0)
        {
            return CompleteTypeNames(partial, namespaces, statics: true);
        }

        var (prefix, typeName) = SplitName(partial[..dot]);
        var memberPartial = partial[(dot + 1)..];
        var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var type = ResolveType(prefix, typeName, namespaces); type != null; type = type.BaseType)
        {
            foreach (var member in type.Members)
            {
                if (member.IsStatic && member.IsPublic &&
                    member.MemberKind is Adamantium.UI.Markup.CodeGeneration.ResolvedMemberKind.Field
                        or Adamantium.UI.Markup.CodeGeneration.ResolvedMemberKind.Property &&
                    Matches(member.Name, memberPartial))
                {
                    names.TryAdd(member.Name, member.MemberType?.Name);
                }
            }
        }

        return names
            .Select(n => new AumlCompletionItem(n.Key, AumlCompletionItemKind.Value, n.Value, ReplaceBack: memberPartial.Length))
            .ToList();
    }

    // {Localize Table.Key, name=value}: the tables, then the table's strings, then the placeholders the string fills.
    private IReadOnlyList<AumlCompletionItem> CompleteLocalize(string text, int offset, IReadOnlyDictionary<string, string> namespaces)
    {
        var end = Math.Min(offset, text.Length);
        var opening = "{" + LocalizeExtension;
        var start = text.LastIndexOf(opening, Math.Max(0, end - 1), StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        // Split at the commas of the extension itself; a caret inside a nested {Binding} is not ours to complete.
        var arguments = new List<string> { "" };
        var depth = 0;
        foreach (var c in text[(start + opening.Length)..end])
        {
            depth += c switch { '{' => 1, '}' => -1, _ => 0 };
            if (c == ',' && depth == 0)
            {
                arguments.Add("");
            }
            else
            {
                arguments[^1] += c;
            }
        }

        if (depth != 0)
        {
            return [];
        }

        var first = arguments[0].Trim();
        if (arguments.Count == 1)
        {
            return CompleteTableOrString(first, namespaces);
        }

        var current = arguments[^1].TrimStart();
        if (current.Contains('='))
        {
            return [];
        }

        var given = arguments.Skip(1).SkipLast(1).Select(a => a.Split('=')[0].Trim()).ToHashSet(StringComparer.Ordinal);
        var dot = first.LastIndexOf('.');
        var (prefix, table) = SplitName(dot < 0 ? first : first[..dot]);
        var target = TablesNamed(prefix, table, namespaces).SelectMany(t => t.Strings).FirstOrDefault(s => dot >= 0 && s.Key == first[(dot + 1)..]);
        return target == null
            ? []
            : target.Parameters
                .Where(p => !given.Contains(p) && Matches(p, current))
                .Select(p => new AumlCompletionItem(p, AumlCompletionItemKind.Property, ServerMessages.Placeholder(), InsertText: p + "=", ReplaceBack: current.Length))
                .ToList();
    }

    private IReadOnlyList<AumlCompletionItem> CompleteTableOrString(string partial, IReadOnlyDictionary<string, string> namespaces)
    {
        var (prefix, name) = SplitName(partial);
        var dot = name.LastIndexOf('.');
        if (dot < 0)
        {
            return TablesNamed(prefix, null, namespaces)
                .Where(t => MatchesStart(t.Name, name))
                .Select(t => t.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => new AumlCompletionItem(n, AumlCompletionItemKind.Element, ServerMessages.LanguageTable(), ReplaceBack: name.Length))
                .ToList();
        }

        var keyPartial = name[(dot + 1)..];
        return TablesNamed(prefix, name[..dot], namespaces)
            .SelectMany(t => t.Strings)
            .Where(s => Matches(s.Key, keyPartial))
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new AumlCompletionItem(s.Key, AumlCompletionItemKind.Value,
                s.Parameters.Count > 0 ? $"({string.Join(", ", s.Parameters)}) {s.Text}".TrimEnd() : s.Text,
                ReplaceBack: keyPartial.Length))
            .ToList();
    }

    // The tables a "[prefix:]Table" can name: by the prefix's namespace when one is written, any table otherwise - the
    // way the build finds them. A null name takes every table.
    private IEnumerable<LanguageTableInfo> TablesNamed(string prefix, string name, IReadOnlyDictionary<string, string> namespaces)
    {
        var tables = _model.LanguageTables.Where(t => name == null || t.Name == name);
        if (prefix.Length == 0)
        {
            return tables;
        }

        var xmlns = ResolveXmlns(prefix, namespaces);
        var inScope = _model.GetElements(xmlns).Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
        return tables.Where(t => inScope.Contains(t.FullName));
    }

    /// <summary>The type named by the nearest <c>&lt;attrLocalName&gt;="..."</c> attribute before the caret
    /// (pragmatic scan): the ControlTemplate's <c>TargetType</c> for a TemplateBinding, or the <c>x:ViewModel</c>
    /// for a Binding. Good enough to scope completion to the enclosing template / data type.</summary>
    private Adamantium.UI.Markup.CodeGeneration.IResolvedType FindNearestAttributeType(
        string text, int offset, string attrLocalName, IReadOnlyDictionary<string, string> namespaces)
    {
        int region = Math.Min(offset, text.Length);
        var head = text.Substring(0, region);
        // Match the attribute by NAME (optionally xmlns-prefixed) followed by ="...". Anchoring on `name="` is what
        // makes this robust: a plain substring search would match the local name inside a VALUE - e.g. "ViewModel"
        // inside "MainViewModel" - and resolve the wrong text, which is exactly why {Binding} completion went blank.
        var matches = Regex.Matches(head, $@"(?:\w+:)?{Regex.Escape(attrLocalName)}\s*=\s*""([^""]*)""");
        if (matches.Count == 0) return null;
        var value = matches[^1].Groups[1].Value.Trim();   // nearest (last) such attribute before the caret
        var (prefix, local) = SplitName(UnwrapTypeExtension(value));
        return ResolveType(prefix, local, namespaces);
    }

    // Accepts both "prefix:Type" and the x:Type markup-extension form "{x:Type prefix:Type}" (positional, space-
    // separated). Without this, a ViewModel written as {x:Type ...} wouldn't resolve and {Binding} path completion
    // would offer nothing.
    private static string UnwrapTypeExtension(string value)
    {
        if (!value.StartsWith('{') || !value.EndsWith('}')) return value;
        var inner = value[1..^1].Trim();
        var space = inner.IndexOf(' ');
        return space < 0 ? inner : inner[(space + 1)..].Trim();
    }

    /// <summary>Completes a (possibly dotted) binding path against <paramref name="dataType"/>: each segment before
    /// the last narrows to that property's type; the last segment is completed against the narrowed type. Uses
    /// readable properties (get-only included), since bindings read as well as write.</summary>
    private IReadOnlyList<AumlCompletionItem> CompleteBindingPath(
        string path, Adamantium.UI.Markup.CodeGeneration.IResolvedType dataType)
    {
        if (dataType is null) return [];

        var type = dataType;
        var memberPartial = path;
        int lastDot = path.LastIndexOf('.');
        if (lastDot >= 0)
        {
            memberPartial = path[(lastDot + 1)..];
            foreach (var seg in path[..lastDot].Split('.'))
            {
                var prop = _model.GetBindableProperties(type).FirstOrDefault(p => p.Name == seg);
                if (prop?.Type is null) return [];
                type = prop.Type;
            }
        }

        return _model.GetBindableProperties(type)
            .Where(p => Matches(p.Name, memberPartial))
            .OrderBy(p => p.Name)
            // ReplaceBack covers only the current path segment, so picking an item replaces just "Sh" (not the whole
            // "{Binding Sh"), and the client filters the list by this segment as you type instead of the whole value.
            .Select(p => new AumlCompletionItem(p.Name, AumlCompletionItemKind.Property, p.Type?.Name, ReplaceBack: memberPartial.Length))
            .ToList();
    }

    private IReadOnlyList<AumlCompletionItem> CompleteElements(AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces,
        string text, int offset)
    {
        var (prefix, partial) = SplitName(ctx.Prefix);

        // Property-element syntax: <Owner.Property> — complete the owner type's settable properties.
        int dot = partial.IndexOf('.');
        if (dot >= 0)
        {
            var owner = ResolveType(prefix, partial[..dot], namespaces);
            if (owner is null) return [];
            var memberPartial = partial[(dot + 1)..];
            var ownerDot = partial[..(dot + 1)];   // "Owner." — kept so the inserted local name stays whole
            return _model.GetProperties(owner, includeReadOnlyCollections: true)
                .Concat(_model.GetAttachedProperties(owner))
                .DistinctBy(p => p.Name)
                .Where(p => Matches(p.Name, memberPartial))
                .OrderBy(p => p.Name)
                .Select(p => new AumlCompletionItem(ownerDot + p.Name, AumlCompletionItemKind.Property, p.Type?.Name,
                    ReplaceBack: partial.Length))
                .ToList();
        }

        var xmlns = ResolveXmlns(prefix, namespaces);
        if (xmlns.Length == 0) return [];

        var fits = ValueFilter(text, offset, namespaces) ?? _model.CanBeElement;
        return _model.GetElements(xmlns)
            .Where(t => MatchesStart(t.Name, partial) && fits(t))
            .OrderBy(t => t.Name)
            .Select(t => new AumlCompletionItem(t.Name, AumlCompletionItemKind.Element))
            .ToList();
    }

    // In a property element - <Border.Background> - only what the property can hold, by the build's own rule: an element
    // its one value can be, or one of its items' type; null outside a property element or when its type is unknown.
    private Func<Adamantium.UI.Markup.CodeGeneration.IResolvedType, bool> ValueFilter(string text, int offset,
        IReadOnlyDictionary<string, string> namespaces)
    {
        var lt = text.LastIndexOf('<', Math.Max(0, Math.Min(offset, text.Length) - 1));
        if (lt < 0 || ParentElement(text, lt) is not { } parent)
        {
            return null;
        }

        var (prefix, name) = SplitName(parent);
        var dot = name.IndexOf('.');
        if (dot < 0 || ResolveType(prefix, name[..dot], namespaces) is not { } owner)
        {
            return null;
        }

        var property = name[(dot + 1)..];
        var type = _model.GetProperties(owner, includeReadOnlyCollections: true).FirstOrDefault(p => p.Name == property)?.Type
                   ?? _model.GetAttachedProperties(owner).FirstOrDefault(p => p.Name == property)?.Type;
        if (type == null)
        {
            return null;
        }

        var many = PropertyValues.TakesMany(type);
        var value = many ? _model.ItemTypeOf(type) : type;
        if (value == null)
        {
            return _model.CanBeCreated;
        }

        // A class made from markup is judged by the class it derives from: the build lets it be anything it cannot see.
        return t => _model.CanBeCreated(t) && (t is MetadataResolvedType { BaseType: { } based } ? based : t) is var probe &&
                    PropertyValues.Fits(probe, value) &&
                    (!many || value.SpecialType == ResolvedSpecialType.System_Object ||
                     !probe.InheritsFromMarkupExtension(Adamantium.UI.Markup.Parsers.AumlParser.MarupExtensionClassFullName));
    }

    // The element whose content a tag starting at lt is in: the innermost one opened before it and not closed.
    private static string ParentElement(string text, int lt)
    {
        var open = new Stack<string>();
        var at = 0;
        while (at < lt)
        {
            var start = text.IndexOf('<', at);
            if (start < 0 || start >= lt)
            {
                break;
            }

            if (string.CompareOrdinal(text, start, "<!--", 0, 4) == 0)
            {
                at = After(text, start, "-->");
                continue;
            }

            if (string.CompareOrdinal(text, start, "<![CDATA[", 0, 9) == 0)
            {
                at = After(text, start, "]]>");
                continue;
            }

            if (start + 1 < text.Length && text[start + 1] is '?' or '!')
            {
                at = After(text, start, ">");
                continue;
            }

            var closing = start + 1 < text.Length && text[start + 1] == '/';
            var nameStart = start + (closing ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] is '_' or '-' or '.' or ':'))
            {
                nameEnd++;
            }

            var end = TagEnd(text, nameEnd);
            if (end < 0 || end >= lt)
            {
                break;
            }

            if (closing)
            {
                open.TryPop(out _);
            }
            else if (text[end - 1] != '/')
            {
                open.Push(text[nameStart..nameEnd]);
            }

            at = end + 1;
        }

        return open.TryPeek(out var parent) ? parent : null;
    }

    private static int After(string text, int from, string marker)
    {
        var at = text.IndexOf(marker, from, StringComparison.Ordinal);
        return at < 0 ? text.Length : at + marker.Length;
    }

    private static int TagEnd(string text, int from)
    {
        var quote = '\0';
        for (var i = from; i < text.Length; i++)
        {
            if (quote != '\0')
            {
                quote = text[i] == quote ? '\0' : quote;
            }
            else if (text[i] is '"' or '\'')
            {
                quote = text[i];
            }
            else if (text[i] == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private IReadOnlyList<AumlCompletionItem> CompleteAttributes(AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces, string text, int offset)
    {
        var (attrPrefix, partial) = SplitName(ctx.Prefix);

        // Attributes already on this element are hidden so completion can't create a duplicate.
        var used = CollectUsedAttributes(text, offset);

        // Attached-property syntax: Owner.Property="..." — complete the owner type's attached properties.
        int dot = partial.IndexOf('.');
        if (dot >= 0)
        {
            var owner = ResolveType(attrPrefix, partial[..dot], namespaces);
            if (owner is null) return [];
            var memberPartial = partial[(dot + 1)..];
            var ownerDot = partial[..(dot + 1)];   // "Owner."
            return _model.GetAttachedProperties(owner)
                .Where(p => Matches(p.Name, memberPartial) && !used.Contains(ownerDot + p.Name))
                .OrderBy(p => p.Name)
                .Select(p => AttrItem(ownerDot + p.Name, AumlCompletionItemKind.Property, p.Type?.Name, partial.Length))
                .ToList();
        }

        // Typing "x:..." (an attribute in the directives namespace) -> offer x: directives only.
        if (attrPrefix.Length > 0 && namespaces.TryGetValue(attrPrefix, out var ns) && ns == AumlXDirectives.Xmlns)
            return AumlXDirectives.All
                .Where(d => Matches(d.Name, partial) && !used.Contains($"{attrPrefix}:{d.Name}"))
                .Select(d => AttrItem(d.Name, AumlCompletionItemKind.Directive, d.Detail))
                .ToList();

        var items = new List<AumlCompletionItem>();

        var element = ResolveElement(ctx.ElementName, namespaces);
        if (element is not null)
            items.AddRange(_model.GetProperties(element)
                .Where(p => Matches(p.Name, ctx.Prefix) && !used.Contains(p.Name))
                .OrderBy(p => p.Name)
                .Select(p => AttrItem(p.Name, AumlCompletionItemKind.Property, p.Type?.Name)));

        // Also surface the x: directives (prefixed) when no prefix is being typed yet.
        var directivePrefix = namespaces.FirstOrDefault(kv => kv.Value == AumlXDirectives.Xmlns).Key;
        if (attrPrefix.Length == 0 && directivePrefix is { Length: > 0 })
            foreach (var d in AumlXDirectives.All)
            {
                var label = $"{directivePrefix}:{d.Name}";
                if (Matches(label, ctx.Prefix) && !used.Contains(label))
                    items.Add(AttrItem(label, AumlCompletionItemKind.Directive, d.Detail));
            }

        return items;
    }

    /// <summary>
    /// Attribute names already present on the element whose opening tag holds the caret, so completion can exclude
    /// them and never offer a duplicate. Scans from the element's '&lt;' to the tag end (quote-aware) and takes the
    /// identifier before each '=' - covering attributes both before and after the caret.
    /// </summary>
    private static HashSet<string> CollectUsedAttributes(string text, int offset)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        int region = Math.Min(offset, text.Length);
        int lt = text.LastIndexOf('<', Math.Max(0, region - 1));
        if (lt < 0) return used;

        int i = lt + 1;
        bool inQuote = false;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote && (c == '>' || (c == '<' && i > lt + 1))) break;
        }

        var tag = text.Substring(lt, i - lt);
        tag = Regex.Replace(tag, "\"[^\"]*\"", m => new string(' ', m.Length));   // blank quoted values so '=' in a value can't look like an attribute
        foreach (Match m in Regex.Matches(tag, @"([A-Za-z_][\w:.\-]*)\s*="))
            used.Add(m.Groups[1].Value);
        return used;
    }

    private IReadOnlyList<AumlCompletionItem> CompleteValues(AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces, string text, int offset, string documentPath)
    {
        // A type-valued x: directive (x:ViewModel) completes type names directly in its value - the simplified form
        // without {x:Type}. Same type set used inside {x:Type}.
        if (IsTypeReferenceDirective(ctx.AttributeName, namespaces))
            return CompleteTypeNames(ctx.Prefix, namespaces);

        if (XDirective(ctx.AttributeName, namespaces) is { Values.Count: > 0 } directive)
        {
            return directive.Values
                .Where(v => Matches(v, ctx.Prefix))
                .Select(v => new AumlCompletionItem(v, AumlCompletionItemKind.Value, ctx.AttributeName))
                .ToList();
        }

        // StrokeDashSymbols: a space-separated sequence of glyph names. Offer the names declared in this element's
        // StrokeDashGlyphs (plus the built-in Dash/Dot), completing the token after the last space.
        if (string.Equals(ctx.AttributeName, "StrokeDashSymbols", StringComparison.Ordinal))
            return CompleteDashSymbols(ctx.Prefix, text, offset);

        var element = ResolveElement(ctx.ElementName, namespaces);
        var propertyType = element is null ? null : _model.GetPropertyType(element, ctx.AttributeName ?? "");
        if (propertyType is null) return [];

        if (propertyType.FullName == "System.Type")
        {
            return CompleteTypeNames(ctx.Prefix, namespaces, element.GetMemberByName(ctx.AttributeName).TypeOfBase());
        }

        if (element.GetMemberByName(ctx.AttributeName).FileExtensions() is { } extensions)
        {
            return CompletePaths(ctx.Prefix, documentPath, extensions);
        }

        return _model.GetValueCompletions(propertyType)
            .Where(v => Matches(v, ctx.Prefix))
            .Select(v => new AumlCompletionItem(v, AumlCompletionItemKind.Value, propertyType.Name, Color: ColorOf(propertyType, v)))
            .ToList();
    }

    // Glyph names for a StrokeDashSymbols value: the built-in Dash/Dot plus any names declared in this element's
    // StrokeDashGlyphs (minus "Gap", the off-length, not a symbol). Completes the token after the last space, so
    // "Dash Do|" replaces just "Do".
    private IReadOnlyList<AumlCompletionItem> CompleteDashSymbols(string value, string text, int offset)
    {
        value ??= "";
        int sp = value.LastIndexOfAny([' ', '\t', '\n', '\r']);
        var token = sp >= 0 ? value[(sp + 1)..] : value;

        var names = new List<string> { "Dash", "Dot" };
        var glyphs = CurrentElementAttributeValue(text, offset, "StrokeDashGlyphs");
        if (glyphs is not null)
            foreach (var pair in glyphs.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                var name = (eq >= 0 ? pair[..eq] : pair).Trim();
                if (name.Length > 0 && !name.Equals("Gap", StringComparison.OrdinalIgnoreCase)
                    && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }

        return names
            .Where(n => Matches(n, token))
            .Select(n => new AumlCompletionItem(n, AumlCompletionItemKind.Value, ServerMessages.DashGlyph(), ReplaceBack: token.Length))
            .ToList();
    }

    // The value of an attribute on the SAME element whose opening tag holds the caret (e.g. read StrokeDashGlyphs while
    // completing StrokeDashSymbols). Scans from the element's '<' to the first '>'. Null when the attribute isn't there.
    private static string CurrentElementAttributeValue(string text, int offset, string attrLocalName)
    {
        int region = Math.Min(offset, text.Length);
        int lt = text.LastIndexOf('<', Math.Max(0, region - 1));
        if (lt < 0) return null;
        int gt = text.IndexOf('>', lt);
        var tag = gt < 0 ? text[lt..] : text[lt..(gt + 1)];
        var m = Regex.Match(tag, $@"(?:\w+:)?{Regex.Escape(attrLocalName)}\s*=\s*""([^""]*)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    // Completes a type reference written as "[prefix:]Partial": offers the types of the prefix's xmlns (a
    // clr-namespace includes its view-models). Shared by {x:Type ...} and the plain type-valued x: directives.
    private IReadOnlyList<AumlCompletionItem> CompleteTypeNames(string prefixText, IReadOnlyDictionary<string, string> namespaces,
        string typeBase = null, bool statics = false)
    {
        var (prefix, partial) = SplitName(prefixText);
        Func<Adamantium.UI.Markup.CodeGeneration.IResolvedType, bool> fits = statics
            ? AumlTypeModel.HasStaticValues
            : t => AumlTypeModel.CanBeReferenced(t) && Fits(t, typeBase);

        // Prefix already typed (e.g. "vm:Ma"): complete within that namespace, replacing only the name part.
        if (prefix.Length > 0)
        {
            var xmlns = ResolveXmlns(prefix, namespaces);
            if (xmlns.Length == 0) return [];
            return _model.GetElements(xmlns)
                .Where(t => Matches(t.Name, partial) && fits(t))
                .OrderBy(t => t.Name)
                .Select(t => new AumlCompletionItem(t.Name, AumlCompletionItemKind.Element, ReplaceBack: partial.Length))
                .ToList();
        }

        // No prefix yet: offer types from EVERY declared namespace so a view-model in a clr-namespace shows while you
        // type its bare name (without it, only the default xmlns was offered, so the list kept vanishing). A type
        // outside the default xmlns is inserted prefix-qualified ("vm:MainViewModel") so it resolves; filterText (the
        // bare name) keeps the client filtering the list as you type.
        var items = new List<AumlCompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ns in namespaces)
        {
            foreach (var t in _model.GetElements(ns.Value))
            {
                if (!MatchesStart(t.Name, partial) || !fits(t)) continue;
                var insert = ns.Key.Length > 0 ? $"{ns.Key}:{t.Name}" : t.Name;
                if (!seen.Add(t.FullName)) continue;
                items.Add(new AumlCompletionItem(t.Name, AumlCompletionItemKind.Element,
                    ns.Key.Length > 0 ? ns.Key : null, InsertText: insert, ReplaceBack: partial.Length));
            }
        }

        foreach (var t in _model.ProjectTypes)
        {
            if (MatchesStart(t.Name, partial) && fits(t) && _model.ResolveShortName(t.Name)?.FullName == t.FullName
                && seen.Add(t.FullName))
            {
                items.Add(new AumlCompletionItem(t.Name, AumlCompletionItemKind.Element, ReplaceBack: partial.Length));
            }
        }

        if (typeBase != null)
        {
            foreach (var t in _model.TypesDerivedFrom(typeBase))
            {
                if (MatchesStart(t.Name, partial) && _model.ResolveShortName(t.Name)?.FullName == t.FullName && seen.Add(t.FullName))
                {
                    items.Add(new AumlCompletionItem(t.Name, AumlCompletionItemKind.Element, ReplaceBack: partial.Length));
                }
            }
        }
        return items.OrderBy(i => i.Label).ToList();
    }

    private static bool Fits(Adamantium.UI.Markup.CodeGeneration.IResolvedType type, string typeBase) =>
        typeBase == null || (type.FullName != typeBase && type.IsAssignableTo(typeBase));

    // True when the attribute is an x: directive whose value names a CLR type (x:ViewModel), per the single-source
    // registry AumlDirectives. Lets the plain "prefix:Type" value complete types, like inside {x:Type}.
    private static bool IsTypeReferenceDirective(string attributeName, IReadOnlyDictionary<string, string> namespaces) =>
        XDirective(attributeName, namespaces) is { IsTypeReference: true };

    private static AumlDirectiveInfo XDirective(string attributeName, IReadOnlyDictionary<string, string> namespaces)
    {
        if (string.IsNullOrEmpty(attributeName))
        {
            return null;
        }

        var colon = attributeName.IndexOf(':');
        if (colon < 0 || !namespaces.TryGetValue(attributeName[..colon], out var ns) || ns != AumlXDirectives.Xmlns)
        {
            return null;
        }

        return AumlDirectives.Find(attributeName[(colon + 1)..]);
    }

    /// <summary>
    /// Completes a (possibly sub-foldered) file path written in an attribute value against the project root — the
    /// directory relative paths load against (the nearest <c>.csproj</c> ancestor of the edited file). The value
    /// so far is split into an already-typed directory part and a final-segment prefix; folders are offered with a
    /// trailing '/' so the path can be drilled into. Returns nothing without a known document/project.
    /// </summary>
    private IReadOnlyList<AumlCompletionItem> CompletePaths(string partial, string documentPath, IReadOnlyList<string> extensions)
    {
        var root = FindProjectRoot(documentPath);
        if (root is null) return [];

        partial = partial.Replace('\\', '/');
        int slash = partial.LastIndexOf('/');
        var dirPart = slash >= 0 ? partial[..(slash + 1)] : "";          // "Textures/" (kept, never re-inserted)
        var namePrefix = slash >= 0 ? partial[(slash + 1)..] : partial;  // the segment being typed

        string lookupDir;
        try { lookupDir = Path.GetFullPath(Path.Combine(root, dirPart)); }
        catch { return []; }
        if (!Directory.Exists(lookupDir)) return [];

        // Replace only the segment typed after the last '/' (so the client filters/replaces on the segment, not the
        // whole "Textures/..." value — otherwise bare file names get filtered out and nothing shows).
        var replaceBack = namePrefix.Length;
        var items = new List<AumlCompletionItem>();
        var below = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var dir in Directory.GetDirectories(lookupDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (name is "bin" or "obj" || name.StartsWith('.') || !Matches(name, namePrefix)
                || !Directory.EnumerateFiles(dir, "*", below).Any(f => HasExtension(f, extensions)))
            {
                continue;
            }

            items.Add(new AumlCompletionItem(name + "/", AumlCompletionItemKind.Value, ServerMessages.Folder(), ReplaceBack: replaceBack));
        }
        foreach (var file in Directory.GetFiles(lookupDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || !Matches(name, namePrefix) || !HasExtension(name, extensions))
            {
                continue;
            }

            items.Add(new AumlCompletionItem(name, AumlCompletionItemKind.Value, ServerMessages.File(), ReplaceBack: replaceBack));
        }
        return items;
    }

    private static bool HasExtension(string file, IReadOnlyList<string> extensions) =>
        extensions.Contains(Path.GetExtension(file).TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    // The project root a relative asset path resolves against: the nearest .csproj ancestor of the edited file.
    internal static string FindProjectRoot(string documentPath)
    {
        if (string.IsNullOrEmpty(documentPath)) return null;
        DirectoryInfo dir;
        try { dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(documentPath))); }
        catch { return null; }
        for (; dir is not null; dir = dir.Parent)
            if (dir.Exists && dir.GetFiles("*.csproj").Length > 0) return dir.FullName;
        return null;
    }

    internal Adamantium.UI.Markup.CodeGeneration.IResolvedType ResolveElement(string qualifiedName, IReadOnlyDictionary<string, string> namespaces)
    {
        var (prefix, local) = SplitName(qualifiedName ?? "");
        var xmlns = ResolveXmlns(prefix, namespaces);
        return xmlns.Length == 0 ? null : _model.GetElement(xmlns, local);
    }

    /// <summary>Resolves a (possibly xmlns-prefixed) type name; an unprefixed name may live in any registered namespace.</summary>
    internal Adamantium.UI.Markup.CodeGeneration.IResolvedType ResolveType(string xmlnsPrefix, string typeName, IReadOnlyDictionary<string, string> namespaces)
    {
        var xmlns = ResolveXmlns(xmlnsPrefix, namespaces);
        if (xmlns.Length > 0 && _model.GetElement(xmlns, typeName) is { } resolved) return resolved;
        return xmlnsPrefix.Length == 0 ? _model.ResolveShortName(typeName) : null;
    }

    private static string ResolveXmlns(string prefix, IReadOnlyDictionary<string, string> namespaces)
    {
        if (namespaces.TryGetValue(prefix, out var uri)) return uri;
        return prefix.Length == 0 ? FallbackXmlns : "";   // unknown prefix -> no namespace
    }

    internal static (string Prefix, string Local) SplitName(string name)
    {
        int colon = name.IndexOf(':');
        return colon < 0 ? ("", name) : (name[..colon], name[(colon + 1)..]);
    }

    // An attribute completion that auto-inserts ="" and drops the caret between the quotes (LSP snippet),
    // so picking a property doesn't leave the author to type =" " themselves.
    private static AumlCompletionItem AttrItem(string label, AumlCompletionItemKind kind, string detail, int? replaceBack = null) =>
        new(label, kind, detail, $"{label}=\"$0\"", replaceBack);

    // Case-insensitive substring match for short scoped lists; large global catalogs use MatchesStart. The client
    // highlights matches.
    private static bool Matches(string candidate, string prefix) =>
        string.IsNullOrEmpty(prefix) || candidate.Contains(prefix, StringComparison.OrdinalIgnoreCase);

    // Prefix match (case-insensitive): for the LARGE, framework-wide catalogs (control names per xmlns, type names
    // across all namespaces) so completion only offers what's in scope as you type the start of the name, instead of
    // every type that merely contains the letters.
    private static bool MatchesStart(string candidate, string prefix) =>
        string.IsNullOrEmpty(prefix) || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
