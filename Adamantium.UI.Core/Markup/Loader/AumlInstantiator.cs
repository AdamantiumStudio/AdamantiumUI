using System.Globalization;
using System.Reflection;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.MarkupExtensions;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.AST.TypeReference;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Markup.CodeGeneration.Reflection;

namespace Adamantium.UI.Core.Markup;

/// <summary>
/// Walks a (already type-resolved) AUML AST and builds a live object tree by reflection: instantiate each
/// element, set its properties (text values, markup extensions, nested objects) and add children via
/// <see cref="IContainer.AddOrSetChildComponent"/>. Non-fatal issues are collected, not thrown, so a partly
/// invalid buffer (mid-edit) still previews what it can.
/// </summary>
internal sealed class AumlInstantiator
{
    private readonly ITypeResolver _resolver;
    private readonly List<Assembly> _assemblies;
    private readonly Func<Type, Type> _typeMapper;
    private readonly List<string> _diagnostics;

    // The template results being built, innermost on top: named parts and TemplateBindings register with it.
    private readonly Stack<TemplateResult> _templates = new();
    private object _root;

    public AumlInstantiator(ITypeResolver resolver, List<Assembly> assemblies, Func<Type, Type> typeMapper, List<string> diagnostics)
    {
        _resolver = resolver;
        _assemblies = assemblies;
        _typeMapper = typeMapper;
        _diagnostics = diagnostics;
    }

    /// <summary>Each instantiated element mapped to its AUML source position (designer go-to-source / hover).
    /// Reference-keyed so controls that override Equals/GetHashCode don't collide.</summary>
    public Dictionary<object, AumlSourceSpan> SourceMap { get; } = new(ReferenceEqualityComparer.Instance);

    public object Instantiate(AumlAstObjectNode node)
    {
        var clrType = ResolveClrType(node.TypeReference);
        if (clrType == null)
        {
            _diagnostics.Add($"Unknown type '{node.TypeReference?.GetFullTypeName()}'");
            return null;
        }

        if (typeof(UiTemplate).IsAssignableFrom(clrType))
        {
            return BuildTemplate(node, clrType);
        }

        var actualType = _typeMapper?.Invoke(clrType) ?? clrType;
        var instance = Activator.CreateInstance(actualType);
        if (instance != null) SourceMap[instance] = new AumlSourceSpan(node.Line, node.Position);
        _root ??= instance;

        foreach (var child in node.Children)
        {
            // One element or property that cannot be built is reported and skipped: it must not take the whole
            // document - and with it the preview - down.
            try
            {
                switch (child)
                {
                    case AumlAstObjectNode objectNode:
                        var childObj = Instantiate(objectNode);
                        if (childObj is IAdamantiumComponent && instance is IContainer container)
                        {
                            container.AddOrSetChildComponent(childObj);
                        }
                        else if (childObj != null && ContentListOf(instance) is { } content)
                        {
                            content.Add(childObj);
                        }

                        break;

                    case AumlAstPropertyNode { Property: AumlAstPropertyReference pref } prop:
                        ApplyProperty(instance, pref, prop);
                        break;

                    case AumlAstTextNode text when !string.IsNullOrWhiteSpace(text.Text):
                        if (instance is IContainer textHost) textHost.AddOrSetChildComponent(text.Text);
                        break;

                    case AumlAstDirective { Name: "Name" } nameDirective:
                        ApplyName(instance, (nameDirective.Value as AumlAstTextNode)?.Text?.Trim());
                        break;
                }
            }
            catch (Exception e)
            {
                _diagnostics.Add($"{DescribeChild(child)} on {actualType.Name}: {InnermostMessage(e)}");
            }
        }

        return instance;
    }

    // x:Name: carried at runtime so {Binding ElementName=...} finds the element, and registered with the template being
    // built so the control finds its named parts - both as the generator emits them.
    private void ApplyName(object instance, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (instance.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true } nameProperty
            && nameProperty.PropertyType == typeof(string))
        {
            nameProperty.SetValue(instance, name);
        }

        if (_templates.TryPeek(out var building))
        {
            if (instance is IAdamantiumComponent part)
            {
                building.RegisterName(name, part);
            }
        }
        else if (_root is IFundamentalUIComponent root)
        {
            NameScope.Register(root, name, instance);
        }
    }

    private static System.Collections.IList ContentListOf(object instance) =>
        instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<ContentAttribute>() != null)
            ?.GetValue(instance) as System.Collections.IList;

    // A template is a factory, as the generator emits it: every Build() makes a fresh copy of its content.
    private object BuildTemplate(AumlAstObjectNode node, Type templateType)
    {
        var content = node.Children.OfType<AumlAstObjectNode>().FirstOrDefault();
        var properties = node.Children.OfType<AumlAstPropertyNode>()
            .Where(p => p.Property is AumlAstPropertyReference)
            .ToList();
        var triggers = properties
            .Where(p => ((AumlAstPropertyReference)p.Property).Name == "Triggers")
            .SelectMany(p => p.Values)
            .OfType<AumlAstObjectNode>()
            .ToList();

        Func<TemplateResult> builder = () => BuildTemplateResult(content, triggers);
        UiTemplate template;
        try
        {
            template = (UiTemplate)Activator.CreateInstance(templateType, [builder]);
        }
        catch (Exception e)
        {
            _diagnostics.Add($"Cannot create template '{templateType.Name}': {InnermostMessage(e)}");
            return null;
        }

        SourceMap[template] = new AumlSourceSpan(node.Line, node.Position);
        if (template is DataTemplate dataTemplate
            && node.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.DataType)?.Value
                is AumlAstTypeReferenceValueNode { TypeReference: { } dataType })
        {
            dataTemplate.DataType = ResolveClrType(dataType);
        }

        // The template's own configuration - ControlTemplate.TargetType, HierarchicalDataTemplate.ItemsSource, ...
        foreach (var prop in properties.Where(p => ((AumlAstPropertyReference)p.Property).Name != "Triggers"))
        {
            try
            {
                ApplyProperty(template, (AumlAstPropertyReference)prop.Property, prop);
            }
            catch (Exception e)
            {
                _diagnostics.Add($"{DescribeChild(prop)} on {templateType.Name}: {InnermostMessage(e)}");
            }
        }

        return template;
    }

    private TemplateResult BuildTemplateResult(AumlAstObjectNode content, List<AumlAstObjectNode> triggers)
    {
        var result = new TemplateResult();
        _templates.Push(result);
        try
        {
            result.RootComponent = content == null ? null : Instantiate(content) as IUIComponent;
            foreach (var triggerNode in triggers)
            {
                if (Instantiate(triggerNode) is ITrigger trigger)
                {
                    result.Triggers.Add(trigger);
                }
            }
        }
        finally
        {
            _templates.Pop();
        }

        return result;
    }

    // Inside a template a part's value is the TEMPLATE's, so the template's own triggers can still change it - the
    // priority the generator gives it.
    private void Assign(object instance, PropertyInfo p, object value)
    {
        if (_templates.Count > 0 && instance is AdamantiumComponent part && part.GetProperty(p.Name) != null)
        {
            part.SetValue(p.Name, value, ValuePriority.Template);
            return;
        }

        p.SetValue(instance, value);
    }

    private static string DescribeChild(IAumlAstNode child) => child switch
    {
        AumlAstPropertyNode { Property: AumlAstPropertyReference pref } => $"Property '{pref.Name}'",
        AumlAstObjectNode obj => $"Element '{obj.TypeReference?.Name}' (line {obj.Line})",
        _ => "Content"
    };

    private static string InnermostMessage(Exception e)
    {
        while (e is TargetInvocationException { InnerException: not null } wrapped)
        {
            e = wrapped.InnerException;
        }

        return e.Message;
    }

    private void ApplyProperty(object instance, AumlAstPropertyReference pref, AumlAstPropertyNode prop)
    {
        if (pref.IsAttachedProperty)
        {
            ApplyAttachedProperty(instance, pref, prop);
            return;
        }

        var p = instance.GetType().GetProperty(pref.Name, BindingFlags.Public | BindingFlags.Instance);
        if (p == null)
        {
            _diagnostics.Add($"Property '{pref.Name}' not found on {instance.GetType().Name}");
            return;
        }

        if (TryBuildResourceDictionary(p.PropertyType, prop, out var dictionary))
        {
            Assign(instance, p, dictionary);
            return;
        }

        // A collection-typed property-element (writable like <Button.Transitions>, or read-only like
        // <Theme.StyleIncludes>): its child ELEMENTS are added to the collection, never assigned - mirroring the
        // generator's IsCollection() path. Without this a writable collection (Transitions has a setter) takes the
        // assignment path below and fails ("DoubleTransition cannot be converted to Transitions").
        if (IsPopulatableCollection(p.PropertyType) && prop.Values.Any(v => v is AumlAstObjectNode))
        {
            PopulateCollection(instance, p, prop);
            return;
        }

        // A read-only collection property-element (e.g. <RenderTargetPanel.Behaviors>) whose declared type isn't a
        // concrete IList/ICollection is never assigned - its child elements are added to the existing collection instance.
        if (!p.CanWrite)
        {
            if (!TryAddToReadOnlyCollection(instance, p, prop))
                _diagnostics.Add($"Property '{pref.Name}' is not writable on {instance.GetType().Name}");
            return;
        }

        // A style/setter/trigger value, or a template's own configuration, keeps markers and bindings as OBJECTS: the
        // setter (or the template) applies them to the element later, as the generator emits it.
        var keepsMarkers = instance is UiTemplate || instance.GetType().Namespace == "Adamantium.UI.Core.Resources";

        foreach (var value in prop.Values)
        {
            // A Binding/MultiBinding sets up a live binding unless the property holds a binding itself. Checked against
            // AdamantiumComponent so bindings inside brushes work.
            if (TryBuildBindingBase(value, out var binding))
            {
                if (keepsMarkers || typeof(BindingBase).IsAssignableFrom(p.PropertyType))
                    Assign(instance, p, binding);
                else if (instance is AdamantiumComponent bindable)
                    bindable.SetBinding(pref.Name, binding);
                else
                    _diagnostics.Add($"'{pref.Name}' on {instance.GetType().Name} cannot be bound");
                continue;
            }

            // {TemplateBinding X}: a part's property follows the templated control's, wired when the template builds.
            if (value is AumlAstMarkupExtensionNode { TypeReference.Name: "TemplateBinding" } tbNode)
            {
                var templateBinding = CreateMarkupObject(tbNode) as TemplateBinding;
                if (templateBinding == null) continue;
                if (keepsMarkers)
                    Assign(instance, p, templateBinding);
                else if (_templates.TryPeek(out var building) && instance is IAdamantiumComponent part)
                    building.AddTemplateBinding(part, pref.Name, templateBinding);
                else
                    _diagnostics.Add($"TemplateBinding on '{pref.Name}' is only meaningful inside a ControlTemplate");
                continue;
            }

            // {ThemeResource Key}: a live link to the active theme's accent/focus property (not a data binding).
            if (!keepsMarkers && instance is IFundamentalUIComponent themed &&
                value is AumlAstMarkupExtensionNode { TypeReference.Name: "ThemeResource" } trNode)
            {
                var key = (trNode.Arguments.FirstOrDefault()?.Value as AumlAstTextNode)?.Text?.Trim();
                if (!string.IsNullOrEmpty(key)) new ThemeResource(key).Apply(themed, pref.Name);
                continue;
            }

            // {ResourceReference Key} on an element's property: deferred like the generator does, so a resource local to
            // the element's subtree resolves once the element is in the tree. A plain CLR property has no slot to defer
            // into and is resolved now, below, as on any markup object.
            if (!keepsMarkers && instance is IAdamantiumComponent referencing &&
                AdamantiumPropertyMap.ResolveProperty(instance.GetType(), pref.Name) != null &&
                value is AumlAstMarkupExtensionNode { TypeReference.Name: "ResourceReference" } rrNode)
            {
                var key = (rrNode.Arguments.FirstOrDefault()?.Value as AumlAstTextNode)?.Text?.Trim();
                var priority = _templates.Count > 0 ? ValuePriority.Template : ValuePriority.Local;
                if (!string.IsNullOrEmpty(key)) ResourceResolver.SetDeferred(referencing, pref.Name, key, priority);
                continue;
            }

            switch (value)
            {
                case AumlAstMarkupExtensionNode markup:
                    var resolved = ResolveMarkupExtension(markup, p.PropertyType, keepsMarkers ? null : instance, pref.Name);
                    if (resolved == null) break;
                    if (p.PropertyType.IsInstanceOfType(resolved)) Assign(instance, p, resolved);
                    // A live marker ({ObservableResource}, {Ancestor}, {Self}) connects itself to the property in
                    // ProvideObject and hands itself back - it is not the value.
                    else if (resolved is not MarkupExtension)
                        _diagnostics.Add($"Cannot assign {resolved.GetType().Name} to '{pref.Name}' ({p.PropertyType.Name})");
                    break;

                case AumlAstNullValueNode:
                    Assign(instance, p, null);
                    break;

                case AumlAstTextNode textNode:
                    if (TryConvert(textNode.Text, p.PropertyType, out var converted))
                        Assign(instance, p, converted);
                    break;

                case AumlAstObjectNode objectNode:
                    var nested = Instantiate(objectNode);
                    if (nested != null) Assign(instance, p, nested);
                    break;

                default:
                    var other = ResolveValue(value, p.PropertyType);
                    if (other != null) Assign(instance, p, other);
                    break;
            }
        }
    }

    // Grid.Row, ToolTipService.ToolTip, ...: set through the owner's static Set{Name}, as the generator emits it. Skipping
    // them laid every grid child out in cell 0,0 and dropped every tooltip from the preview.
    private void ApplyAttachedProperty(object instance, AumlAstPropertyReference pref, AumlAstPropertyNode prop)
    {
        var owner = ResolveClrType(pref.OwnerType);
        var setter = owner?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Set" + pref.Name
                && m.GetParameters() is { Length: 2 } parameters
                && parameters[0].ParameterType.IsInstanceOfType(instance));
        if (setter == null)
        {
            _diagnostics.Add($"Attached property '{pref.OwnerType?.Name}.{pref.Name}' not found for {instance.GetType().Name}");
            return;
        }

        var valueType = setter.GetParameters()[1].ParameterType;
        if (TryBuildResourceDictionary(valueType, prop, out var dictionary))
        {
            setter.Invoke(null, [instance, dictionary]);
            return;
        }

        foreach (var value in prop.Values)
        {
            if (instance is AdamantiumComponent bindable && TryBuildBindingBase(value, out var binding))
            {
                if (owner.GetField(pref.Name + "Property", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is AdamantiumProperty property)
                {
                    bindable.SetBinding(property, binding);
                }
                else
                {
                    _diagnostics.Add($"Attached property '{owner.Name}.{pref.Name}' has no {pref.Name}Property to bind");
                }

                continue;
            }

            var resolved = ResolveValue(value, valueType);
            if (resolved != null || value is AumlAstNullValueNode)
            {
                setter.Invoke(null, [instance, resolved]);
            }
        }
    }

    // <X.Resources> written as children: a dictionary of the keyed entries declared right there, with <ResourceLink>s
    // pulling dictionary files in - the dictionary the generator builds and hands to the setter.
    private bool TryBuildResourceDictionary(Type propertyType, AumlAstPropertyNode prop, out ResourceDictionary dictionary)
    {
        dictionary = null;
        if (!typeof(ResourceDictionary).IsAssignableFrom(propertyType) || !prop.Values.Any(v => v is AumlAstObjectNode))
        {
            return false;
        }

        dictionary = new ResourceDictionary();
        foreach (var entry in prop.Values.OfType<AumlAstObjectNode>())
        {
            try
            {
                var value = Instantiate(entry);
                if (value is ResourceLink link)
                {
                    dictionary.Includes.Add(link);
                    continue;
                }

                var key = entry.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == "Key")?.Value as AumlAstTextNode;
                if (string.IsNullOrEmpty(key?.Text))
                {
                    _diagnostics.Add($"An inline resource '{entry.TypeReference?.Name}' (line {entry.Line}) needs an x:Key");
                    continue;
                }

                dictionary.Add(key.Text.Trim(), value);
            }
            catch (Exception e)
            {
                _diagnostics.Add($"Resource '{entry.TypeReference?.Name}' (line {entry.Line}): {InnermostMessage(e)}");
            }
        }

        return true;
    }

    /// <summary>A property whose declared type is a concrete collection (non-generic IList/ICollection, as
    /// <c>List&lt;T&gt;</c> is) and isn't parsed from text via [TypeParser] - mirrors the generator's IsCollection().</summary>
    private static bool IsPopulatableCollection(Type t) =>
        t != typeof(string)
        && (typeof(System.Collections.IList).IsAssignableFrom(t) || typeof(System.Collections.ICollection).IsAssignableFrom(t)
            || IsGenericCollection(t))
        && t.GetCustomAttribute<TypeParserAttribute>() == null;

    // The generator's check matches ICollection<T> too (a Roslyn symbol's name carries no arity), so e.g.
    // RibbonContextualGroups - a TrackingCollection<T> - is filled, not assigned.
    private static bool IsGenericCollection(Type t) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));

    /// <summary>
    /// Populates a collection-typed property by ADDING each child element (mirrors the generator's IsCollection path):
    /// a settable collection (e.g. Transitions) gets a fresh instance, a read-only one (e.g. RowDefinitions) is added
    /// to in place. Per-child failures are reported individually so a partly-valid buffer still previews.
    /// </summary>
    private void PopulateCollection(object instance, PropertyInfo p, AumlAstPropertyNode prop)
    {
        object collection;
        if (p.CanWrite)
        {
            try { collection = Activator.CreateInstance(p.PropertyType); }
            catch { _diagnostics.Add($"Cannot create collection for '{p.Name}'"); return; }
            p.SetValue(instance, collection);
        }
        else
        {
            collection = p.GetValue(instance);
            if (collection == null) { _diagnostics.Add($"Collection '{p.Name}' is null on {instance.GetType().Name}"); return; }
        }

        var addMethods = collection.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Add" && m.GetParameters().Length == 1)
            .ToArray();

        foreach (var value in prop.Values)
        {
            if (value is not AumlAstObjectNode objectNode) continue;
            var child = Instantiate(objectNode);
            if (child == null) continue;

            var add = addMethods.FirstOrDefault(m => m.GetParameters()[0].ParameterType.IsInstanceOfType(child));
            if (add != null) add.Invoke(collection, [child]);
            else _diagnostics.Add($"Cannot add {child.GetType().Name} to {p.Name}");
        }
    }

    /// <summary>
    /// Populates a read-only collection property (no setter, e.g. Behaviors / Theme.StyleIncludes) by adding each
    /// child object element to the existing collection instance via its single-argument <c>Add</c>. Returns false
    /// when the property isn't a populatable collection (no instance, or no matching Add) so the caller can report it.
    /// </summary>
    private bool TryAddToReadOnlyCollection(object instance, PropertyInfo p, AumlAstPropertyNode prop)
    {
        var collection = p.GetValue(instance);
        if (collection == null) return false;

        var addMethods = collection.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Add" && m.GetParameters().Length == 1)
            .ToArray();
        if (addMethods.Length == 0) return false;

        foreach (var value in prop.Values)
        {
            if (value is not AumlAstObjectNode objectNode) continue;
            var child = Instantiate(objectNode);
            if (child == null) continue;

            var add = addMethods.FirstOrDefault(m => m.GetParameters()[0].ParameterType.IsInstanceOfType(child));
            if (add != null) add.Invoke(collection, [child]);
            else _diagnostics.Add($"Cannot add {child.GetType().Name} to {p.Name}");
        }
        return true;   // a populatable read-only collection; per-child issues are reported individually
    }

    // ---- bindings -------------------------------------------------------------------------------------------------

    // A Binding or MultiBinding written either as a markup extension ({Binding ...}/{MultiBinding ...}) or as an
    // element (<Binding .../>, <MultiBinding>...</MultiBinding>). Returns false for anything that isn't a binding.
    private bool TryBuildBindingBase(IAumlAstNode node, out BindingBase binding)
    {
        binding = node switch
        {
            AumlAstMarkupExtensionNode me => me.TypeReference?.Name switch
            {
                "Binding" => BuildBindingFromMarkup(me),
                "MultiBinding" => BuildMultiBindingFromMarkup(me),
                _ => null,
            },
            AumlAstObjectNode obj => obj.TypeReference?.Name switch
            {
                "Binding" => BuildBindingFromObject(obj),
                "MultiBinding" => BuildMultiBindingFromObject(obj),
                _ => null,
            },
            AumlAstLocalizedStringNode localized => BuildLocalized(localized),
            _ => null,
        };
        return binding != null;
    }

    private BindingBase BuildLocalized(AumlAstLocalizedStringNode node)
    {
        LocalizedStrings table = null;
        if (node.TableSource == null)
        {
            var type = (_resolver.Resolve(node.TableFullName) as ReflectionResolvedType)?.ClrType;
            table = Localize.TableOf(type);
            if (table == null)
            {
                _diagnostics.Add($"The language table {node.TableFullName} is not built yet: build the project to preview its strings");
                return null;
            }
        }

        var localize = new Localize(table, node.Key);
        if (node.TableSource != null)
        {
            localize.TableSource = Followed(node.TableSource);
        }

        if (node.KeySource != null)
        {
            localize.KeySource = Followed(node.KeySource);
        }

        foreach (var argument in node.Arguments)
        {
            localize.Arguments[argument.Name] = Followed(argument.Value);
        }

        return localize;

        object Followed(IAumlAstValueNode value) => value switch
        {
            _ when TryBuildBindingBase(value, out var binding) => binding,
            AumlAstMarkupExtensionNode { TypeReference.Name: "TemplateBinding" } templateBinding =>
                CreateMarkupObject(templateBinding),
            _ => value?.GetTextValue(),
        };
    }

    private Binding BuildBindingFromMarkup(AumlAstMarkupExtensionNode me)
    {
        var binding = new Binding();
        foreach (var arg in me.Arguments)
        {
            if (string.IsNullOrEmpty(arg.Name))   // positional = Path ({Binding User.Name})
            {
                var path = (arg.Value as AumlAstTextNode)?.Text?.Trim();
                if (!string.IsNullOrEmpty(path)) binding.Path = new PropertyPath(path);
            }
            else ApplyBindingArg(binding, arg.Name, arg.Value);
        }
        return binding;
    }

    private Binding BuildBindingFromObject(AumlAstObjectNode obj)
    {
        var binding = new Binding();
        foreach (var child in obj.Children)
            if (child is AumlAstPropertyNode { Property: AumlAstPropertyReference pref } pn && pn.Values.Count > 0)
                ApplyBindingArg(binding, pref.Name, pn.Values[0]);
        return binding;
    }

    private MultiBinding BuildMultiBindingFromMarkup(AumlAstMarkupExtensionNode me)
    {
        var multi = new MultiBinding();
        foreach (var arg in me.Arguments)
        {
            if (string.IsNullOrEmpty(arg.Name))                       // positional = a child binding
            {
                if (TryBuildBindingBase(arg.Value, out var child)) multi.Bindings.Add(child);
            }
            else ApplyMultiBindingArg(multi, arg.Name, arg.Value);
        }
        return multi;
    }

    private MultiBinding BuildMultiBindingFromObject(AumlAstObjectNode obj)
    {
        var multi = new MultiBinding();
        foreach (var child in obj.Children)
        {
            switch (child)
            {
                case AumlAstObjectNode childObj when TryBuildBindingBase(childObj, out var nested):
                    multi.Bindings.Add(nested);                       // direct child <Binding/>/<MultiBinding/>
                    break;
                case AumlAstPropertyNode { Property: AumlAstPropertyReference pref } pn when pn.Values.Count > 0:
                    if (pref.Name == "Bindings")                       // explicit <MultiBinding.Bindings> element
                    {
                        foreach (var v in pn.Values)
                            if (TryBuildBindingBase(v, out var listed)) multi.Bindings.Add(listed);
                    }
                    else ApplyMultiBindingArg(multi, pref.Name, pn.Values[0]);
                    break;
            }
        }
        return multi;
    }

    private void ApplyBindingArg(Binding binding, string name, IAumlAstValueNode value)
    {
        switch (name)
        {
            case "Path": if ((value as AumlAstTextNode)?.Text is { } p) binding.Path = new PropertyPath(p.Trim()); break;
            case "Mode": if (TryEnum<BindingMode>(value, out var mode)) binding.Mode = mode; break;
            case "Converter": if (ResolveValue(value, typeof(IValueConverter)) is IValueConverter c) binding.Converter = c; break;
            case "ConverterParameter": binding.ConverterParameter = ResolveValue(value, typeof(object)); break;
            case "Source": binding.Source = ResolveValue(value, typeof(object)); break;
            case "StringFormat": binding.StringFormat = (value as AumlAstTextNode)?.Text; break;
            case "FallbackValue": binding.FallbackValue = ResolveValue(value, typeof(object)); break;
            case "TargetNullValue": binding.TargetNullValue = ResolveValue(value, typeof(object)); break;
            case "ElementName": if ((value as AumlAstTextNode)?.Text is { } element) binding.ElementName = element.Trim(); break;
            default: _diagnostics.Add($"Unknown Binding property '{name}'"); break;
        }
    }

    private void ApplyMultiBindingArg(MultiBinding multi, string name, IAumlAstValueNode value)
    {
        switch (name)
        {
            case "Mode": if (TryEnum<BindingMode>(value, out var mode)) multi.Mode = mode; break;
            case "Converter": if (ResolveValue(value, typeof(IMultiValueConverter)) is IMultiValueConverter c) multi.Converter = c; break;
            case "ConverterParameter": multi.ConverterParameter = ResolveValue(value, typeof(object)); break;
            case "StringFormat": multi.StringFormat = (value as AumlAstTextNode)?.Text; break;
            case "FallbackValue": multi.FallbackValue = ResolveValue(value, typeof(object)); break;
            case "TargetNullValue": multi.TargetNullValue = ResolveValue(value, typeof(object)); break;
            default: _diagnostics.Add($"Unknown MultiBinding property '{name}'"); break;
        }
    }

    private static bool TryEnum<T>(IAumlAstValueNode value, out T result) where T : struct
    {
        result = default;
        var text = (value as AumlAstTextNode)?.Text;
        if (!TypeCastFactory.TryParseEnum(typeof(T), text, out var parsed)) return false;

        result = (T)parsed;
        return true;
    }

    // Resolves a value node: markup extension, inline element or literal. Directives such as {x:Null} and {x:Type} arrive
    // already transformed into their nodes.
    private object ResolveValue(IAumlAstValueNode value, Type targetType) => value switch
    {
        AumlAstNullValueNode => null,
        AumlAstTypeReferenceValueNode typeNode => ResolveTypeValue(typeNode),
        AumlAstStaticMemberValueNode staticMember => ResolveStaticMember(staticMember),
        AumlAstMarkupExtensionNode me => ResolveMarkupExtension(me, targetType),
        AumlAstObjectNode obj => Instantiate(obj),
        AumlAstTextNode text => TryConvert(text.Text, targetType, out var r) ? r : null,
        _ => null,
    };

    // {x:Static Type.Member} in the preview. The transformer has already proved the member exists, so a miss here means
    // the preview resolved a different type than the build would - worth saying, not swallowing.
    private object ResolveStaticMember(AumlAstStaticMemberValueNode node)
    {
        var owner = ResolveClrType(node.TypeReference);
        if (owner == null)
        {
            _diagnostics.Add($"x:Static type '{node.TypeReference?.GetFullTypeName()}' could not be resolved");
            return null;
        }

        var field = owner.GetField(node.MemberName, BindingFlags.Public | BindingFlags.Static);
        if (field != null)
        {
            return field.GetValue(null);
        }

        var property = owner.GetProperty(node.MemberName, BindingFlags.Public | BindingFlags.Static);
        if (property != null)
        {
            return property.GetValue(null);
        }

        _diagnostics.Add($"x:Static: '{owner.FullName}' has no static member '{node.MemberName}'");
        return null;
    }

    private Type ResolveTypeValue(AumlAstTypeReferenceValueNode node)
    {
        var resolved = ResolveClrType(node.TypeReference);
        if (resolved == null)
        {
            _diagnostics.Add($"x:Type '{node.TypeReference?.GetFullTypeName()}' could not be resolved");
        }

        return resolved;
    }

    // Positional arguments of the markers that take more than a key, in the order the generator reads them.
    private static readonly Dictionary<string, string[]> PositionalProperties = new(StringComparer.Ordinal)
    {
        ["Ancestor"] = ["AncestorType", "Path"],
        ["Self"] = ["Path"],
        ["TemplateBinding"] = ["Path"],
    };

    private object ResolveMarkupExtension(AumlAstMarkupExtensionNode markup, Type targetType,
        object targetObject = null, string targetProperty = null)
    {
        var name = markup.TypeReference?.Name ?? string.Empty;

        // {ResourceReference Key} -> ResourceResolver.Resolve<targetType>(Key) (what the generator emits).
        if (name.StartsWith("ResourceReference"))
        {
            var key = (markup.Arguments.FirstOrDefault()?.Value as AumlAstTextNode)?.Text;
            if (string.IsNullOrEmpty(key)) return null;

            try
            {
                var method = typeof(ResourceResolver).GetMethod(nameof(ResourceResolver.Resolve))?.MakeGenericMethod(targetType);
                return method?.Invoke(null, [key]);
            }
            catch { return null; }
        }

        // General markup extension - e.g. a converter authored AS a MarkupExtension ({local:MyConverter}). Instantiate
        // it with its arguments, then call ProvideObject (a converter's ProvideObject typically returns itself; a live
        // marker connects itself to the target property).
        var instance = CreateMarkupObject(markup);
        var context = new MarkupContext { TargetObject = targetObject, TargetPropertyName = targetProperty };
        return instance is MarkupExtension ext ? ext.ProvideObject(context) : instance;
    }

    // The markup extension object with its positional and named arguments set, before ProvideObject.
    private object CreateMarkupObject(AumlAstMarkupExtensionNode markup)
    {
        var name = markup.TypeReference?.Name ?? string.Empty;
        var clrType = ResolveClrType(markup.TypeReference);
        if (clrType == null)
        {
            _diagnostics.Add($"Markup extension '{name}' is not supported in the preview");
            return null;
        }

        var positional = markup.Arguments.Where(a => string.IsNullOrEmpty(a.Name)).ToList();
        PositionalProperties.TryGetValue(name, out var positionalNames);

        object instance;
        try
        {
            // {ObservableResource Key}, {ThemeResource Key}: a single positional argument is the constructor's key.
            var key = positionalNames == null && positional.Count == 1 ? (positional[0].Value as AumlAstTextNode)?.Text?.Trim() : null;
            instance = key != null && clrType.GetConstructor([typeof(string)]) != null
                ? Activator.CreateInstance(clrType, key)
                : Activator.CreateInstance(clrType);
        }
        catch { _diagnostics.Add($"Cannot create markup extension '{name}'"); return null; }

        for (var i = 0; i < positional.Count && positionalNames != null && i < positionalNames.Length; i++)
        {
            SetExtensionArgument(instance, clrType, positionalNames[i], positional[i].Value);
        }

        foreach (var arg in markup.Arguments.Where(a => !string.IsNullOrEmpty(a.Name)))
        {
            SetExtensionArgument(instance, clrType, arg.Name, arg.Value);
        }

        return instance;
    }

    private void SetExtensionArgument(object instance, Type clrType, string name, IAumlAstValueNode value)
    {
        var prop = clrType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not { CanWrite: true })
        {
            _diagnostics.Add($"Markup extension '{clrType.Name}' has no settable '{name}'");
            return;
        }

        var argValue = ResolveValue(value, prop.PropertyType);
        if (argValue != null) prop.SetValue(instance, argValue);
    }

    private bool TryConvert(string text, Type targetType, out object result)
    {
        result = null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (t == typeof(string)) { result = text; return true; }
            // An object-typed property (e.g. Setter.Value="Auto") takes the raw string in markup; the real
            // conversion happens later when the value is applied to its concrete target property.
            if (t == typeof(object)) { result = text; return true; }
            if (t.IsEnum) { result = TypeCastFactory.ParseEnum(t, text); return true; }
            if (t == typeof(bool)) { result = bool.Parse(text); return true; }
            if (t.IsPrimitive || t == typeof(decimal))
            {
                result = Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
                return true;
            }

            if (typeof(Brush).IsAssignableFrom(t))
            {
                var field = typeof(Brushes).GetField(text, BindingFlags.Public | BindingFlags.Static);
                if (field != null) { result = field.GetValue(null); return true; }
            }

            // A type named by its short name ({Ancestor Border}, Stop=ScrollViewer), as the generator resolves it.
            if (t == typeof(Type))
            {
                result = (_resolver.ResolveByShortName(text.Trim()) as ReflectionResolvedType)?.ClrType;
                if (result != null) return true;
                _diagnostics.Add($"Type '{text}' could not be resolved");
                return false;
            }

            var parse = t.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null);
            if (parse != null) { result = parse.Invoke(null, [text]); return true; }

            // Last resort: the engine's TypeParser, which honors [TypeParser] attributes and the ParserRegistry.
            // This is exactly what the compiled code-behind generator emits (TypeParser.Parse<T>), so the live
            // preview converts the same value types a build does - e.g. a Path's SVG "Data" string into a Geometry
            // via GeometryParser (Geometry has no static Parse, so without this it stayed null and crashed the renderer).
            result = TypeParser.Parse(text, t);
            return true;
        }
        catch (Exception e)
        {
            _diagnostics.Add($"Cannot convert '{text}' to {t.Name}: {InnermostMessage(e)}");
            return false;
        }
    }

    private Type ResolveClrType(IAumlAstTypeReference typeRef)
    {
        if (typeRef == null) return null;

        if (_resolver.Resolve(typeRef.GetFullTypeName()) is ReflectionResolvedType resolved)
            return resolved.ClrType;

        // Children of a non-entity root (a bare <StackPanel>) arrive unresolved; fall back to the transformer's short-name
        // lookup.
        if (_resolver.ResolveByShortName(typeRef.Name) is ReflectionResolvedType byShortName)
            return byShortName.ClrType;

        return _assemblies
            .SelectMany(ReflectionResolvedAssembly.SafeGetTypes)
            .FirstOrDefault(x => x.Name == typeRef.Name && x.Namespace == typeRef.Namespace);
    }

    // ==== hot reload: live-tree reconciliation ====================================================================
    // Diffs oldNode (what the live tree was built from) against newNode and applies only the changes in place.

    public void Reconcile(object live, AumlAstObjectNode oldNode, AumlAstObjectNode newNode)
    {
        if (live == null || oldNode == null || newNode == null) return;
        ReconcileProperties(live, oldNode, newNode);
        ReconcileChildren(live, oldNode, newNode);
    }

    private void ReconcileProperties(object live, AumlAstObjectNode oldNode, AumlAstObjectNode newNode)
    {
        var oldProps = PropertyNodes(oldNode);
        var newProps = PropertyNodes(newNode);

        // Removed (present in the old markup, gone in the new): clear back to default.
        foreach (var name in oldProps.Keys)
            if (!newProps.ContainsKey(name)) ResetProperty(live, name);

        // Added / changed: re-apply ONLY when the value actually changed, so unchanged properties don't re-fire their
        // transitions (re-applying every property on each edit is exactly what made "the whole tree restart").
        foreach (var (name, newProp) in newProps)
        {
            if (oldProps.TryGetValue(name, out var oldProp) && NodeSignature(oldProp) == NodeSignature(newProp))
                continue;
            ApplyPropertyNode(live, newProp);
        }
    }

    private void ReconcileChildren(object live, AumlAstObjectNode oldNode, AumlAstObjectNode newNode)
    {
        if (live is not IContainer container) return;

        var oldKids = ObjectChildren(oldNode);
        var newKids = ObjectChildren(newNode);
        // No child elements before or after: what the live container holds came from an attribute or a binding (a
        // Button's Content="Add", a list's ItemsSource), so there is nothing here to splice - rebuilding wiped it.
        if (oldKids.Count == 0 && newKids.Count == 0) return;

        var liveKids = container.GetChildComponents();

        // The live children were built from oldKids in order. If that no longer lines up (e.g. a container that didn't
        // expose its children), we can't splice precisely - rebuild just this container's content.
        if (liveKids.Count != oldKids.Count)
        {
            RebuildChildren(container, newKids);
            return;
        }

        // Key each old child to its live instance; a new child with the same key reuses (and reconciles) it.
        var oldKeys = ChildKeys(oldKids);
        var newKeys = ChildKeys(newKids);
        var byKey = new Dictionary<string, (AumlAstObjectNode Node, object Instance)>(StringComparer.Ordinal);
        for (var i = 0; i < oldKids.Count; i++)
            byKey[oldKeys[i]] = (oldKids[i], liveKids[i]);

        // The desired final child instances, in the NEW order: reuse+reconcile matches, instantiate the rest.
        var desired = new List<object>(newKids.Count);
        for (var j = 0; j < newKids.Count; j++)
        {
            if (byKey.TryGetValue(newKeys[j], out var match))
            {
                Reconcile(match.Instance, match.Node, newKids[j]);
                desired.Add(match.Instance);
            }
            else
            {
                var created = Instantiate(newKids[j]);
                if (created != null) desired.Add(created);
            }
        }

        ApplyChildOrder(container, desired);
    }

    // Splices the container's children to match `desired` (a mix of reused live instances and freshly built ones),
    // preserving reused instances (so their animations keep running) and inserting/removing/reordering minimally.
    private static void ApplyChildOrder(IContainer container, List<object> desired)
    {
        // Drop live children no longer wanted (back-to-front to keep indices valid).
        var current = container.GetChildComponents();
        for (var i = current.Count - 1; i >= 0; i--)
            if (!desired.Any(d => ReferenceEquals(d, current[i])))
                container.RemoveChildComponentAt(i);

        // Position each desired child, moving reused instances and inserting new ones.
        for (var i = 0; i < desired.Count; i++)
        {
            current = container.GetChildComponents();
            if (i < current.Count && ReferenceEquals(current[i], desired[i])) continue;

            var existing = -1;
            for (var k = 0; k < current.Count; k++)
                if (ReferenceEquals(current[k], desired[i])) { existing = k; break; }
            if (existing >= 0) container.RemoveChildComponentAt(existing);
            container.InsertChildComponent(i, desired[i]);
        }
    }

    private void RebuildChildren(IContainer container, List<AumlAstObjectNode> newKids)
    {
        container.RemoveAllChildComponents();
        foreach (var kid in newKids)
        {
            var created = Instantiate(kid);
            if (created is IAdamantiumComponent) container.AddOrSetChildComponent(created);
        }
    }

    private void ApplyPropertyNode(object instance, AumlAstPropertyNode prop)
    {
        if (prop.Property is AumlAstPropertyReference pref) ApplyProperty(instance, pref, prop);
    }

    private static void ResetProperty(object instance, string name)
    {
        if (instance is not AdamantiumComponent comp) return;
        var p = comp.GetProperty(name);
        if (p != null) comp.ClearValue(p);   // clears the Local value -> back to default / style
    }

    private static Dictionary<string, AumlAstPropertyNode> PropertyNodes(AumlAstObjectNode node)
    {
        var result = new Dictionary<string, AumlAstPropertyNode>(StringComparer.Ordinal);
        foreach (var child in node.Children)
            if (child is AumlAstPropertyNode { Property: AumlAstPropertyReference pref } prop && !pref.IsAttachedProperty)
                result[pref.Name] = prop;
        return result;
    }

    private static List<AumlAstObjectNode> ObjectChildren(AumlAstObjectNode node)
    {
        var result = new List<AumlAstObjectNode>();
        foreach (var child in node.Children)
            if (child is AumlAstObjectNode obj) result.Add(obj);
        return result;
    }

    // A stable identity for each child: x:Name (or a Name property) when present, else type + occurrence index among
    // same-typed siblings. Lets reorders/renames reuse the same live instance instead of rebuilding it.
    private static List<string> ChildKeys(List<AumlAstObjectNode> kids)
    {
        var keys = new List<string>(kids.Count);
        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kid in kids)
        {
            var type = kid.TypeReference?.Name ?? "?";
            var name = NameKeyOf(kid);
            if (!string.IsNullOrEmpty(name)) { keys.Add(type + "#" + name); continue; }
            var n = typeCounts.TryGetValue(type, out var c) ? c : 0;
            typeCounts[type] = n + 1;
            keys.Add(type + "@" + n);
        }
        return keys;
    }

    private static string NameKeyOf(AumlAstObjectNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is AumlAstDirective { Name: "Name" } dir) return (dir.Value as AumlAstTextNode)?.Text;
            if (child is AumlAstPropertyNode { Property: AumlAstPropertyReference { Name: "Name" } } p)
                return (p.Values.FirstOrDefault() as AumlAstTextNode)?.Text;
        }
        return null;
    }

    // A textual signature of an AST node so old vs new property values can be compared for "did it actually change".
    private static string NodeSignature(IAumlAstNode node) => node switch
    {
        AumlAstPropertyNode p => "P:" + (p.Property is AumlAstPropertyReference r ? r.Name : "") + "=" +
                                 string.Join("|", p.Values.Select(NodeSignature)),
        AumlAstTextNode t => "T:" + t.Text,
        AumlAstMarkupExtensionNode m => "M:" + (m.TypeReference?.Name ?? "") + "(" +
                                        string.Join(",", m.Arguments.Select(a => a.Name + "=" + NodeSignature(a.Value))) + ")",
        AumlAstObjectNode o => "O:" + (o.TypeReference?.Name ?? "") + "{" +
                               string.Join(";", o.Children.Select(NodeSignature)) + "}",
        AumlAstDirective d => "D:" + d.Name + "=" + NodeSignature(d.Value),
        _ => node?.ToString() ?? ""
    };
}
