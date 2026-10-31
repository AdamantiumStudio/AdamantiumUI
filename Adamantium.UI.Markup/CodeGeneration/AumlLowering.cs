using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.Building;
using Adamantium.UI.Markup.Localization;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>Turns a transformed markup document into its build plan: every decision about what the markup means is
/// made here, once, for the generated code and the runtime loader alike.</summary>
public class AumlLowering
{
    public string ParentName { get; private set; }
    public Stack<string> ElementStack { get; }
    private readonly Stack<string> _templateStack = new();
    private readonly BuildPlanWriter _writer;
    private readonly bool _rootIsThis;

    public Stack<IResolvedType> TargetTypeContext { get; }

    /// <summary>What the processed elements build, in order.</summary>
    public BuildPlan Plan { get; } = new();

    public AumlMetadataContainer Metadata { get; }
    public int Id { get; set; }

    public EntityType EntityType { get; }

    private AumlLowering(AumlMetadataContainer metadata, EntityType entityType)
    {
        Id = 1;
        ElementStack = new();
        TargetTypeContext = new();
        Metadata = metadata;
        EntityType = entityType;
        _writer = new BuildPlanWriter(Plan);
        _rootIsThis = entityType != EntityType.Unknown;
    }

    /// <summary>The build plan of a transformed document. A document with a class of its own builds into <c>this</c>;
    /// a fragment (a bare panel, a template, a value) has none, so its plan builds the root and returns it.</summary>
    public static BuildPlan Lower(AumlMetadataContainer container, IDiagnosticSink diagnostics)
    {
        var isResource = container.RootEntityType is EntityType.ResourceDictionary or EntityType.StyleSet
            or EntityType.Theme or EntityType.ThemeVariant;
        var lowering = new AumlLowering(container, container.RootEntityType);
        var root = lowering.ProcessControlElements((AumlAstObjectNode)container.RootNode, diagnostics, isResource);
        if (!lowering._rootIsThis)
        {
            lowering._writer.Add(new ReturnStep(root));
        }

        return lowering.Plan;
    }

    public string GenerateNextElementName(string name = "") =>
        string.IsNullOrEmpty(name) ? $"element_{Id++}" : $"{name}_{Id++}";

    public void PushElement(string name)
    {
        ElementStack.Push(name);
        ParentName = name;
    }

    public string PeekElement() => ElementStack.Count > 0 ? ElementStack.Peek() : null;

    public void PopElement()
    {
        ElementStack.Pop();
        ParentName = ElementStack.Count > 0 ? ElementStack.Peek() : string.Empty;
    }

    private void PushTemplate(string templateVar) => _templateStack.Push(templateVar);
    private void PopTemplate() { if (_templateStack.Count > 0) _templateStack.Pop(); }
    private string CurrentTemplate => _templateStack.Count > 0 ? _templateStack.Peek() : null;

    public void PushTypeContext(IResolvedType resolvedType)
    {
        TargetTypeContext.Push(resolvedType);
    }

    public void PopTypeContext()
    {
        TargetTypeContext.Pop();
    }

    private IResolvedType GetEffectiveTargetType(IResolvedType fallback)
    {
        if (TargetTypeContext.Count > 0)
            return TargetTypeContext.Peek();
        return fallback;
    }

    public void WithElement(string name, Action action)
    {
        PushElement(name);
        try
        {
            action();
        }
        finally
        {
            PopElement();
        }
    }

    public string CurrentParent => PeekElement();

    public BuildValue ProcessControlElements(
        AumlAstObjectNode element,
        IDiagnosticSink diagnostics,
        bool isResource)
    {
        var typeContainer = Metadata.TypeResolver.GetResolvedAssembly(element.TypeReference.Assembly);
        if (typeContainer == null)
        {
            diagnostics.ReportError(Metadata.ClassName, MarkupMessages.AssemblyOfElementNotResolved(element.TypeReference.Assembly,
                element.TypeReference.Name, element.TypeReference.Namespace, element.TypeReference.IsResolved), element);
            return Variable(string.Empty);
        }
        var typeInfo = typeContainer.GetTypeByShortName(element.TypeReference.Name);
        var properties = element.GetProperties();
        var children = element.GetLogicalChildrenObjects();

        // A UiTemplate (DataTemplate/ControlTemplate/...) as a standalone element - e.g. a keyed entry in a
        // ResourceDictionary - carries its content ONLY through a builder Func. Build it via the builder form (below),
        // not as a plain `new()` with orphaned children, which produces an empty template that renders nothing.
        var isTemplate = typeInfo != null
                         && Metadata.DefaultTypeContainer.UiTemplate != null
                         && typeInfo.InheritsFrom(Metadata.DefaultTypeContainer.UiTemplate.FullName);

        string elementName = string.Empty;
        bool isRoot = _rootIsThis && element == Metadata.RootNode;

        if (isRoot)
        {
            elementName = "this";
            _writer.Open(new ElementStep(element, elementName));
            EmitDesignSource(elementName, element);
        }

        if (!isRoot)
        {
            var isNamed = Metadata.NamedElementsMap.TryGetValue(element, out var named);
            var isHeldBack = GetLoadDirective(element) != null;
            elementName = isNamed && !isHeldBack
                ? named
                : GenerateNextElementName();
            _writer.Open(new ElementStep(element, elementName));
            // Named elements in a control file assign their backing field; templates, resource files and x:Load elements
            // use a local.
            var hasBackingField = isNamed
                && CurrentTemplate == null
                && !isHeldBack
                && EntityType is not (EntityType.ResourceDictionary or EntityType.StyleSet or EntityType.Theme
                                      or EntityType.ThemeVariant);

            // A value-type element with text content (<Color x:Key="X">#33FFFFFF</Color>) is parsed from that text, so it
            // can be a keyed resource.
            var valueText = typeInfo is { TypeKind: ResolvedTypeKind.Struct or ResolvedTypeKind.Enum }
                ? element.Children.OfType<AumlAstTextNode>().FirstOrDefault()?.Text?.Trim()
                : null;

            if (isTemplate)
            {
                EmitTemplateBuilder(elementName, hasBackingField, typeInfo, element, isResource, diagnostics);
            }
            else if (!string.IsNullOrEmpty(valueText))
            {
                ReportIfUnparsable(typeInfo, diagnostics, $"<{typeInfo.Name}>{valueText}</{typeInfo.Name}>");
                _writer.Add(new DeclareStep(elementName, ParseCall(typeInfo, valueText), hasBackingField));
            }
            else
            {
                _writer.Add(new DeclareStep(elementName, new NewValue(Type(typeInfo)), hasBackingField));
                EmitDesignSource(elementName, element);
                // Carry the x:Name at runtime too, so {Binding ..., ElementName=X} resolves it by walking the tree (a view
                // exposes named elements as fields; Name is what an ElementName binding matches on). Only for types that
                // HAVE a Name property - a named non-UI object (e.g. a Transform an animation targets) does not.
                if (isNamed && typeInfo.GetMemberByName("Name") is { MemberType: not null })
                {
                    _writer.Add(new AssignStep(Variable(elementName), "Name", Unescaped(named)));
                }
            }
            if (isNamed && CurrentTemplate != null)
            {
                _writer.Add(Invoke(Variable(CurrentTemplate), "RegisterName", Unescaped(named), Variable(elementName)));
            }
            else if (isNamed && EntityType is not (EntityType.ResourceDictionary or EntityType.StyleSet or EntityType.Theme
                                                   or EntityType.ThemeVariant))
            {
                _writer.Add(Invoke(Type(NameScopeFqn), "Register", VariableValue.This, Unescaped(named), Variable(elementName)));
            }
        }

        var key = KeyOf(element);

        // x:Shared="False" on a KEYED entry: the dictionary stores a FACTORY, not the object, so each ask builds its own.
        // Emitted here, before the element is constructed - once its statements are out there is nothing to wrap.
        if (isResource && key != null && (IsPerTarget(element) || IsPerTargetType(element)))
        {
            var factoryName = GenerateNextElementName("shared");
            _writer.Add(new DeclareStep(factoryName, EmitPerTargetValue(element, false, diagnostics)));
            _writer.Add(Invoke(null, "Add", key, Variable(factoryName)));
            _writer.Close();
            return Variable(factoryName);
        }

        PushTypeContext(typeInfo);

        void ProcessProperties(IEnumerable<AumlAstPropertyNode> propertyNodes)
        {
            foreach (var prop in EachInItsPropertyStep(propertyNodes))
            {
                var propRef = (AumlAstPropertyReference)prop.Property;
                var symbolTarget = isRoot ? null : Variable(CurrentParent);
                // Property type it's always a type of the parent object for the current property.
                // It will always be one level up.
                // For ex. TargetType property -> ControlTemplate
                // This means that for the property "TargetType" inside the ControlTemplate class, the current parent type will be ControlTemplate
                var propertyType = GetEffectiveTargetType(typeInfo);
                var propertyName = propRef.Name;
                if (propRef.IsAttachedProperty)
                {
                    // An attached property's owner (Grid, ToolTipService, ResourceContext, ...) can live in a DIFFERENT
                    // assembly than the element it's set on, so resolve it from ITS OWN assembly - not the element's
                    // container, which only happened to work while the owner shared the element's assembly (e.g. Grid.Row
                    // on a Controls element). ResourceContext.Source on a View (owner in Adamantium.UI.Core) NRE'd here.
                    var ownerContainer = Metadata.TypeResolver.GetResolvedAssembly(propRef.OwnerType.Assembly);
                    propertyType = ownerContainer?.GetTypeByShortName(propRef.OwnerType.Name)
                                   ?? typeContainer.GetTypeByShortName(propRef.OwnerType.Name);
                    // We are faced with Attached property, so its really not a property, but a method with Get and Set prefixes,
                    // so we need to handle this case properly
                    propertyName = $"Get{propRef.Name}";
                    //symbolName = $"{propRef.OwnerType.GetFullTypeName()}.Set{propRef.Name}";
                }

                var resolvedMember = propertyType.GetMemberByName(propertyName);
                var resolvedType = resolvedMember.MemberType;

                if (resolvedType == null)
                {
                    diagnostics.ReportError(Metadata.ClassName,
                        MarkupMessages.UnknownPropertyOnType(propRef.Name, propertyType.FullName), propRef);
                    continue;
                }

                if (prop.Values.Count == 0)
                {
                    continue;
                }

                if (prop.Values.Count == 1 && prop.Values[0].IsTextNode()
                    && MarkupValueChecks.Problem(resolvedType, prop.GetTextValue()) is { } problem)
                {
                    diagnostics.ReportError(Metadata.ClassName,
                        $"{propRef.Name}=\"{prop.GetTextValue()}\" (line {prop.Line}): {problem}", propRef);
                    continue;
                }

                if (resolvedMember.MemberKind == ResolvedMemberKind.Event)
                {
                    _writer.Add(new SubscribeStep(symbolTarget, propRef.Name, prop.GetTextValue()));
                    continue;
                }

                var value = prop.Values[0];

                // {x:Null} - state NOTHING, on purpose. What turns a themed default off: a style setter puts a value in,
                // and with no way to write "none" the only way back would be a second property whose whole job is to say
                // "ignore the first one". Handled before the type lookup below: nothing has no type.
                if (value is AumlAstNullValueNode)
                {
                    var nullTarget = isRoot ? "this" : CurrentParent;
                    if (propRef.IsAttachedProperty)
                    {
                        _writer.Add(Invoke(OwnerOf(propRef), $"Set{propRef.Name}", Variable(nullTarget), NullValue.Instance));
                    }
                    else if (typeInfo != null && typeInfo.ImplementsInterface("IAdamantiumComponent"))
                    {
                        _writer.Add(Invoke(Variable(nullTarget), "SetValue", Unescaped(propRef.Name), NullValue.Instance));
                    }
                    else
                    {
                        _writer.Add(new AssignStep(Variable(nullTarget), propRef.Name, NullValue.Instance));
                    }

                    continue;
                }

                if (value is AumlAstLocalizedStringNode localized)
                {
                    if (IsBindingTypedProperty(resolvedType) && !HoldsAnyBinding(resolvedType))
                    {
                        diagnostics.ReportError(Metadata.ClassName,
                            MarkupMessages.LocalizeIntoBindingProperty(element.TypeReference.Name, propRef.Name, resolvedType.Name), propRef);
                        continue;
                    }

                    var localizedVar = EmitLocalizedBinding(localized, diagnostics, isResource);
                    if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources" || HoldsAnyBinding(resolvedType))
                    {
                        _writer.Add(new AssignStep(symbolTarget, propRef.Name, localizedVar));
                    }
                    else
                    {
                        _writer.Add(EmitSetBinding(isRoot ? "this" : CurrentParent, propRef, localizedVar));
                    }

                    continue;
                }

                var valueTypeName = value.TypeReference.GetFullTypeName();
                var valueResolvedType = Metadata.TypeResolver.Resolve(valueTypeName);

                // A ResourceDictionary property authored as children (keyed objects and <ResourceLink>s): build and fill a
                // dictionary here; children build with isResource:false.
                if (Metadata.DefaultTypeContainer.ResourceDictionary != null && resolvedType != null
                    && (resolvedType.FullName == Metadata.DefaultTypeContainer.ResourceDictionary.FullName
                        || resolvedType.InheritsFrom(Metadata.DefaultTypeContainer.ResourceDictionary.FullName))
                    && !value.IsTextNode())
                {
                    var rdVar = GenerateNextElementName("res");
                    _writer.Add(new DeclareStep(rdVar, new NewValue(Type(Metadata.DefaultTypeContainer.ResourceDictionary))));
                    foreach (var entry in prop.Values.OfType<AumlAstObjectNode>())
                    {
                        // A <ResourceLink> child is not an entry but a LINK to a dictionary FILE, so it goes to Includes
                        // instead of being keyed in: one block pulls in a palette, an icon set and its own entries alike.
                        var entryType = Metadata.TypeResolver.Resolve(entry.TypeReference.GetFullTypeName());
                        if (Metadata.DefaultTypeContainer.ResourceLink != null && entryType != null
                            && entryType.FullName == Metadata.DefaultTypeContainer.ResourceLink.FullName)
                        {
                            var linkName = ProcessControlElements(entry, diagnostics, isResource: false);
                            _writer.Add(Invoke(new MemberValue(Variable(rdVar), "Includes"), "Add", linkName));
                            continue;
                        }

                        var entryKey = KeyOf(entry);
                        if (IsScopeStyle(entry))
                        {
                            var styleName = ProcessControlElements(entry, diagnostics, isResource: false);
                            _writer.Add(Invoke(new MemberValue(Variable(rdVar), "Styles"), "Add", styleName));
                            continue;
                        }

                        // x:Shared="False" works here exactly as it does in a resource FILE: store a factory so every ask
                        // builds its own object. An inline dictionary is where an icon (one visual, many templates) lives,
                        // and a visual has ONE parent - without this the second user of the key would steal it.
                        var entryName = IsPerTarget(entry) || IsPerTargetType(entry)
                            ? EmitPerTargetValue(entry, false, diagnostics)
                            : ProcessControlElements(entry, diagnostics, isResource: false);
                        if (entryKey != null)
                        {
                            _writer.Add(Invoke(Variable(rdVar), "Add", entryKey, entryName));
                        }
                        else
                        {
                            diagnostics.ReportError(Metadata.ClassName,
                                MarkupMessages.InlineResourceNeedsKey(propRef.Name), propRef);
                        }
                    }

                    if (propRef.IsAttachedProperty)
                    {
                        var setTarget = isRoot ? "this" : CurrentParent;
                        _writer.Add(Invoke(OwnerOf(propRef), $"Set{propRef.Name}", Variable(setTarget), Variable(rdVar)));
                    }
                    else
                    {
                        _writer.Add(new AssignStep(symbolTarget, propRef.Name, Variable(rdVar)));
                    }
                    continue;
                }

                // ControlTemplate, DataTemplate (ItemTemplate/ContentTemplate) and ItemsPanelTemplate all derive UiTemplate
                // and take a Func<TemplateResult> builder: emit a builder method that constructs the template's tree and
                // returns a TemplateResult, then `new <Template>(builder)`. Without this a DataTemplate would be emitted as
                // an empty `new DataTemplate()` whose Build() NREs (no builder, no container).
                if (valueResolvedType != null
                    && Metadata.DefaultTypeContainer.UiTemplate != null
                    && valueResolvedType.InheritsFrom(Metadata.DefaultTypeContainer.UiTemplate.FullName))
                {
                    var templateName = GenerateNextElementName("template");
                    EmitTemplateBuilder(templateName, false, valueResolvedType, value, isResource, diagnostics);
                    // A template assigned to a PART's property inside a ControlTemplate goes to TEMPLATE priority (not the CLR
                    // setter's LOCAL), so a theme's own Template setter/trigger can still override it - same rule as every
                    // other templated-part property. Outside a template it stays a plain assignment.
                    if (propRef.IsAttachedProperty)
                    {
                        // An ATTACHED template (Ribbon.QuickAccessTemplate on a Slider) belongs to its owner's setter,
                        // not to the element - the element has no such property and the assignment did not compile.
                        var attachedTarget = isRoot ? "this" : CurrentParent;
                        _writer.Add(Invoke(OwnerOf(propRef), $"Set{propRef.Name}", Variable(attachedTarget), Variable(templateName)));
                    }
                    else if (CurrentTemplate != null && typeInfo != null && typeInfo.ImplementsInterface("IAdamantiumComponent")
                             && HasAdamantiumProperty(typeInfo, propRef.Name))
                    {
                        var target = isRoot ? "this" : CurrentParent;
                        _writer.Add(Invoke(Variable(target), "SetValue", Unescaped(propRef.Name), Variable(templateName), Priority("Template")));
                    }
                    else
                    {
                        var target = isRoot ? "this" : CurrentParent;
                        _writer.Add(new AssignStep(Variable(target), propRef.Name, Variable(templateName)));
                    }
                    continue;
                }
                else if (value is AumlAstMarkupExtensionLiteral literal)
                {
                    var typeContainer = Metadata.TypeResolver.GetResolvedAssembly(literal.TypeReference.Assembly);
                    var typeInfo = typeContainer.GetTypeByShortName(literal.TypeReference.Name);
                    var literalName = GenerateNextElementName();
                    _writer.Add(new DeclareStep(literalName, new NewValue(Type(typeInfo))));
                }
                else if (value is AumlAstMarkupExtensionNode extension)
                {
                    switch (extension.TypeReference.Name)
                    {
                        case "ResourceReference":
                        {
                            var key = extension.Arguments[0].Value.GetTextValue();
                            // Setter and trigger values store a ResourceReference marker, resolved tree-scoped when
                            // applied; in any file, not only resource files.
                            if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name,
                                    new NewValue(Type(Metadata.DefaultTypeContainer.ResourceReference), Unescaped(key))));
                            }
                            else if (typeInfo != null && !propRef.IsAttachedProperty
                                     && (!typeInfo.ImplementsInterface("IAdamantiumComponent") || !HasAdamantiumProperty(typeInfo, propRef.Name)))
                            {
                                // A plain markup object (a template selector, a converter), or a plain CLR property of a
                                // component: no property store to defer into, so resolve the key now and assign it.
                                var target = isRoot ? "this" : CurrentParent;
                                var resolveNow = new CallValue(Type(Metadata.DefaultTypeContainer.ResourceResolver),
                                    "ResolveNow", Unescaped(key));
                                _writer.Add(new AssignStep(Variable(target), propRef.Name,
                                    new CastValue(Type(resolvedType), resolveNow, innerParenthesized: true)));
                            }
                            else
                            {
                                // A direct property: deferred via ResourceResolver.SetDeferred, at Template priority inside
                                // a ControlTemplate (so triggers can override) and Local otherwise, like a literal. An
                                // attached one is named with its owner, the only way the element can find it.
                                var target = isRoot ? "this" : CurrentParent;
                                var deferredPriority = Priority(CurrentTemplate != null ? "Template" : "Local");
                                _writer.Add(Invoke(Type(Metadata.DefaultTypeContainer.ResourceResolver), "SetDeferred",
                                    Variable(target), Unescaped(PropertyPath(propRef)), Unescaped(key), deferredPriority));
                            }

                            break;
                        }
                        case "ThemeResource":
                        {
                            // {ThemeResource Key} -> a live binding to the active theme's property. As a Setter value
                            // it's stored as the marker (Setter.Apply -> SetBinding), in any file - a style written in a
                            // view too; as a normal property it's bound now.
                            var key = extension.Arguments[0].Value.GetTextValue();
                            var themeResource = new NewValue(Type(Metadata.DefaultTypeContainer.ThemeResource), Unescaped(key));
                            if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, themeResource));
                            }
                            else if (IsPlainProperty(typeInfo, propRef))
                            {
                                ReportLiveResourceOnPlainProperty(diagnostics, "ThemeResource", key, typeInfo, propRef);
                            }
                            else
                            {
                                var trVar = GenerateNextElementName("tr");
                                _writer.Add(new DeclareStep(trVar, themeResource));
                                var trTarget = isRoot ? "this" : CurrentParent;
                                _writer.Add(Invoke(Variable(trVar), "Apply", Variable(trTarget), Unescaped(PropertyPath(propRef))));
                            }

                            break;
                        }
                        case "ObservableResource":
                        {
                            // {ObservableResource Key} -> a LIVE, tree-scoped keyed-resource reference (re-resolves on a
                            // theme swap / dictionary load-unload). As a Setter/trigger value it's stored as the marker
                            // (Setter.Apply / the trigger activator call Apply), in any file; as a normal property it's
                            // connected now.
                            var keyValue = extension.Arguments[0].Value;
                            BuildValue observable;
                            string key;
                            string keyed = null;
                            if (IsBindingNode(keyValue))
                            {
                                key = "{Binding}";
                                keyed = GenerateNextElementName("or");
                                _writer.Add(new DeclareStep(keyed, new NewValue(Type(Metadata.DefaultTypeContainer.ObservableResource))));
                                _writer.Add(new AssignStep(Variable(keyed), "KeySource", EmitBinding(keyValue, diagnostics, isResource)));
                                observable = Variable(keyed);
                            }
                            else
                            {
                                key = keyValue.GetTextValue();
                                observable = new NewValue(Type(Metadata.DefaultTypeContainer.ObservableResource), Unescaped(key));
                            }

                            if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, observable));
                            }
                            else if (IsPlainProperty(typeInfo, propRef))
                            {
                                ReportLiveResourceOnPlainProperty(diagnostics, "ObservableResource", key, typeInfo, propRef);
                            }
                            else
                            {
                                var orVar = keyed ?? GenerateNextElementName("or");
                                if (keyed == null)
                                {
                                    _writer.Add(new DeclareStep(orVar, observable));
                                }

                                var orTarget = isRoot ? "this" : CurrentParent;
                                _writer.Add(Invoke(Variable(orVar), "Apply", Variable(orTarget), Unescaped(PropertyPath(propRef))));
                            }

                            break;
                        }
                        case "TemplateBinding":
                        {
                            var tbVar = EmitTemplateBinding(extension);

                            if (isResource && element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                // {TemplateBinding X} as a Setter value (a trigger overriding a part). Store it as the
                                // marker; the trigger resolves it against the templated parent when it fires - see
                                // PropertyTriggerActivator.ApplyTemplateBinding. (Distinct from a normal template
                                // element binding below, which wires up immediately at template build.)
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, tbVar));
                            }
                            else if (CurrentTemplate == null)
                            {
                                diagnostics.ReportError(Metadata.ClassName,
                                    MarkupMessages.TemplateBindingOutsideTemplate(), extension);
                            }
                            else
                            {
                                _writer.Add(Invoke(Variable(CurrentTemplate), "AddTemplateBinding",
                                    Variable(CurrentParent), Unescaped(PropertyPath(propRef)), tbVar));
                            }

                            break;
                        }
                        case "Binding":
                        case "MultiBinding":
                        {
                            var bindingVar = EmitBinding(extension, diagnostics, isResource);
                            if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                // {Binding} as a Setter/trigger value: store the binding OBJECT so Setter.Apply establishes
                                // it as a live binding on the styled element (resolved against ITS DataContext) - this is
                                // what lets an ItemContainerStyle bind a generated container's props from the data item.
                                // Mirrors the ResourceReference/Ancestor/Self markers above.
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, bindingVar));
                            }
                            else if (IsBindingTypedProperty(resolvedType))
                            {
                                // The property itself HOLDS a binding object (e.g. DataTrigger.Binding) - assign it.
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, bindingVar));
                            }
                            else
                            {
                                // Ordinary property: establish a live binding on it.
                                var bindingTarget = isRoot ? "this" : CurrentParent;
                                _writer.Add(EmitSetBinding(bindingTarget, propRef, bindingVar));
                            }
                            break;
                        }
                        case "Ancestor":
                        {
                            // {Ancestor Type, Path, Skip=, Name=, Stop=, Logical=, Mode=} -> resolve the ancestor type
                            // (short name -> typeof, preserving is-a) and wire a live ancestor binding via .Apply, which
                            // (re)resolves against the tree on attach. Works in a ControlTemplate, a DataTemplate, or on a
                            // plain element - target is just the current element.
                            var acVar = GenerateNextElementName("anc");
                            var ancestor = Variable(acVar);
                            _writer.Add(new DeclareStep(acVar, new NewValue(Type(Qualified(extension.TypeReference.GetFullTypeName())))));

                            var positional = 0;
                            foreach (var argument in extension.Arguments)
                            {
                                var text = argument.Value is AumlAstTypeReferenceValueNode typeValue
                                    ? typeValue.TypeReference.Name
                                    : argument.Value.GetTextValue();
                                if (string.IsNullOrEmpty(argument.Name))
                                {
                                    if (positional == 0)
                                    {
                                        var resolved = ResolveTypeArgument(argument.Value);
                                        if (resolved == null)
                                        {
                                            diagnostics.ReportError(Metadata.ClassName, MarkupMessages.AncestorTypeNotResolved(text), extension);
                                        }
                                        else
                                        {
                                            _writer.Add(new AssignStep(ancestor, "AncestorType", new TypeOfValue(Type(resolved))));
                                        }
                                    }
                                    else if (positional == 1)
                                    {
                                        _writer.Add(new AssignStep(ancestor, "Path", Unescaped(text)));
                                    }
                                    positional++;
                                }
                                else
                                {
                                    switch (argument.Name)
                                    {
                                        case "Path": _writer.Add(new AssignStep(ancestor, "Path", Unescaped(text))); break;
                                        case "Skip": _writer.Add(new AssignStep(ancestor, "Skip", new LiteralValue(text))); break;
                                        case "Name": _writer.Add(new AssignStep(ancestor, "Name", Unescaped(text))); break;
                                        case "Logical": _writer.Add(new AssignStep(ancestor, "Logical", new LiteralValue(text.ToLowerInvariant()))); break;
                                        case "Stop":
                                        {
                                            var st = ResolveTypeArgument(argument.Value);
                                            if (st == null)
                                            {
                                                diagnostics.ReportError(Metadata.ClassName, MarkupMessages.AncestorStopTypeNotResolved(text), extension);
                                            }
                                            else
                                            {
                                                _writer.Add(new AssignStep(ancestor, "Stop", new TypeOfValue(Type(st))));
                                            }

                                            break;
                                        }
                                        case "Mode":
                                            _writer.Add(new AssignStep(ancestor, "Mode",
                                                new MemberValue(Type(Qualified(argument.Value.TypeReference.GetFullTypeName())), text)));
                                            break;
                                        case "Converter":
                                            var acConv = EmitValueExpression(argument.Value, ValueConverterFqn, diagnostics, isResource);
                                            if (acConv != null)
                                            {
                                                _writer.Add(new AssignStep(ancestor, "Converter", acConv));
                                            }
                                            else
                                            {
                                                diagnostics.ReportError(Metadata.ClassName, MarkupMessages.AncestorConverterInvalid(), extension);
                                            }

                                            break;
                                        case "ConverterParameter":
                                            _writer.Add(new AssignStep(ancestor, "ConverterParameter",
                                                EmitValueExpression(argument.Value, "object", diagnostics, isResource) ?? Unescaped(text)));
                                            break;
                                        case "FallbackValue":
                                            _writer.Add(new AssignStep(ancestor, "FallbackValue",
                                                EmitValueExpression(argument.Value, "object", diagnostics, isResource) ?? Unescaped(text)));
                                            break;
                                        case "TargetNullValue":
                                            _writer.Add(new AssignStep(ancestor, "TargetNullValue",
                                                EmitValueExpression(argument.Value, "object", diagnostics, isResource) ?? Unescaped(text)));
                                            break;
                                        default:
                                            diagnostics.ReportError(Metadata.ClassName, MarkupMessages.AncestorUnknownArgument(argument.Name), extension);
                                            break;
                                    }
                                }
                            }

                            if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                // {Ancestor ...} as a Setter/trigger value: store the marker; Setter.Apply (or the trigger
                                // activator) calls .Apply against each matched element when the setter fires.
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, ancestor));
                            }
                            else
                            {
                                var acTarget = isRoot ? "this" : CurrentParent;
                                _writer.Add(Invoke(ancestor, "Apply", Variable(acTarget), Unescaped(propRef.Name)));
                            }
                            break;
                        }
                        case "Self":
                        {
                            // {Self Path, Mode=} -> bind a target property to another property on the SAME element.
                            var slfVar = GenerateNextElementName("self");
                            var self = Variable(slfVar);
                            _writer.Add(new DeclareStep(slfVar, new NewValue(Type(Qualified(extension.TypeReference.GetFullTypeName())))));
                            foreach (var argument in extension.Arguments)
                            {
                                var text = argument.Value.GetTextValue();
                                if (string.IsNullOrEmpty(argument.Name) || argument.Name == "Path")
                                {
                                    _writer.Add(new AssignStep(self, "Path", Unescaped(text)));
                                    continue;
                                }

                                switch (argument.Name)
                                {
                                    case "Mode":
                                        _writer.Add(new AssignStep(self, "Mode",
                                            new MemberValue(Type(Qualified(argument.Value.TypeReference.GetFullTypeName())), text)));
                                        break;
                                    case "Converter":
                                        var slfConv = EmitValueExpression(argument.Value, ValueConverterFqn, diagnostics, isResource);
                                        if (slfConv != null)
                                        {
                                            _writer.Add(new AssignStep(self, "Converter", slfConv));
                                        }
                                        else
                                        {
                                            diagnostics.ReportError(Metadata.ClassName, MarkupMessages.SelfConverterInvalid(), extension);
                                        }

                                        break;
                                    case "ConverterParameter":
                                        _writer.Add(new AssignStep(self, "ConverterParameter",
                                            EmitValueExpression(argument.Value, "object", diagnostics, isResource) ?? Unescaped(text)));
                                        break;
                                    case "FallbackValue":
                                        _writer.Add(new AssignStep(self, "FallbackValue",
                                            EmitValueExpression(argument.Value, "object", diagnostics, isResource) ?? Unescaped(text)));
                                        break;
                                    case "TargetNullValue":
                                        _writer.Add(new AssignStep(self, "TargetNullValue",
                                            EmitValueExpression(argument.Value, "object", diagnostics, isResource) ?? Unescaped(text)));
                                        break;
                                    default:
                                        diagnostics.ReportError(Metadata.ClassName, MarkupMessages.SelfUnknownArgument(argument.Name), extension);
                                        break;
                                }
                            }
                            if (element.TypeReference.Namespace == "Adamantium.UI.Core.Resources")
                            {
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, self));
                            }
                            else
                            {
                                var slfTarget = isRoot ? "this" : CurrentParent;
                                _writer.Add(Invoke(self, "Apply", Variable(slfTarget), Unescaped(propRef.Name)));
                            }
                            break;
                        }
                        default:
                            var nestedName = ProcessNestedValue(extension, diagnostics, isResource);
                            if (propRef.IsAttachedProperty)
                            {
                                _writer.Add(Invoke(OwnerOf(propRef), $"Set{propRef.Name}", Variable(elementName), nestedName));
                            }
                            else
                            {
                                _writer.Add(new AssignStep(OwnerOf(propRef), propRef.Name, nestedName));
                            }
                            break;
                    }
                }
                else if (propRef.IsAttachedProperty)
                {
                    // Convert a literal to the attached property's type like a regular property; route non-text values
                    // (e.g. {x:Static}) the regular way too.
                    var expr = prop.Values.Count == 1 && !prop.Values[0].IsTextNode()
                        ? ProcessNestedValue(prop.Values[0], diagnostics, isResource)
                        : BuildValueExpression(prop.GetTextValue(), resolvedType, diagnostics,
                            $"{propRef.Name}=\"{prop.GetTextValue()}\" (line {prop.Line})");
                    // The ROOT element has no parent variable - it IS the generated class, as the object-valued path
                    // below already knows. Writing CurrentParent unguarded emitted `SetX(, value)` for an attached
                    // property authored on the root (ResourceContext.Scope on a Theme).
                    var attachedTarget = isRoot ? "this" : CurrentParent;

                    // Inside a ControlTemplate write at Template priority, not via the Local CLR setter, so triggers can
                    // override it.
                    if (CurrentTemplate != null)
                    {
                        _writer.Add(Invoke(Variable(attachedTarget), "SetValue",
                            new MemberValue(OwnerOf(propRef), $"{propRef.Name}Property"), expr, Priority("Template")));
                    }
                    else
                    {
                        _writer.Add(Invoke(OwnerOf(propRef), $"Set{propRef.Name}", Variable(attachedTarget), expr));
                    }
                }
                // A collection given child elements: new + Add per child. The string form is a text node and goes through
                // the TypeParser; a collection may support both.
                else if (resolvedType.IsCollection() && !value.IsTextNode())
                {
                    if (resolvedMember.MemberKind == ResolvedMemberKind.Property && resolvedMember.HasSetter())
                    {
                        _writer.Add(new AssignStep(symbolTarget, propRef.Name, new NewValue(Type(resolvedType))));
                    }

                    foreach (var propertyValue in prop.Values)
                    {
                        var nestedName = ProcessNestedValue(propertyValue, diagnostics, isResource);
                        _writer.Add(Invoke(new MemberValue(symbolTarget, propRef.Name), "Add", nestedName));
                    }
                }
                else if (resolvedType.FullName == "System.Type")
                {
                    var type = Metadata.TypeResolver.Resolve(prop.GetFullTypeValue());
                    if (type == null)
                    {
                        diagnostics.ReportError(Metadata.ClassName, MarkupMessages.PropertyTypeNotFound(prop.GetTextValue(), propRef.Name), propRef);
                        continue;
                    }
                    _writer.Add(new AssignStep(symbolTarget, propRef.Name, new TypeOfValue(Type(type))));
                }
                else if (value.IsTextNode())
                {
                    var where = $"{propRef.Name}=\"{prop.GetTextValue()}\" (line {prop.Line})";
                    // Template parts get their literals at Template priority so triggers can override them; plain CLR
                    // objects in triggers keep ordinary assignments.
                    if (CurrentTemplate != null && typeInfo.ImplementsInterface("IAdamantiumComponent")
                        && HasAdamantiumProperty(typeInfo, propRef.Name))
                    {
                        var target = isRoot ? "this" : CurrentParent;
                        var expr = BuildValueExpression(prop.GetTextValue(), resolvedType, diagnostics, where);
                        _writer.Add(Invoke(Variable(target), "SetValue", Unescaped(propRef.Name),
                            new CastValue(Type(resolvedType), expr, innerParenthesized: true), Priority("Template")));
                    }
                    else
                    {
                        _writer.Add(new AssignStep(symbolTarget, propRef.Name,
                            BuildValueExpression(prop.GetTextValue(), resolvedType, diagnostics, where)));
                    }
                }
                else
                {
                    foreach (var propertyValue in prop.Values)
                    {
                        // <X.Y><Binding/></X.Y> or <X.Y><MultiBinding>...</MultiBinding></X.Y> -> a live binding, unless the
                        // property itself holds a binding object (e.g. DataTrigger.Binding), in which case assign it.
                        if (IsBindingNode(propertyValue))
                        {
                            var bindingVar = EmitBinding(propertyValue, diagnostics, isResource);
                            if (IsBindingTypedProperty(resolvedType))
                            {
                                _writer.Add(new AssignStep(symbolTarget, propRef.Name, bindingVar));
                            }
                            else
                            {
                                var bindingTarget = isRoot ? "this" : CurrentParent;
                                _writer.Add(EmitSetBinding(bindingTarget, propRef, bindingVar));
                            }
                            continue;
                        }
                        // x:Shared="False" on the value: hand the setter a FACTORY instead of an instance, so every target
                        // gets its own. Emitted before the generic path, which would build the one shared object.
                        if (IsPerTarget(propertyValue)
                            || (propRef.Name == "Value" && typeInfo.ImplementsInterface("ISetter") && IsPerTargetType(propertyValue)))
                        {
                            _writer.Add(new AssignStep(symbolTarget, propRef.Name, EmitPerTargetValue(propertyValue, isResource, diagnostics)));
                            continue;
                        }

                        var nestedName = ProcessNestedValue(propertyValue, diagnostics, isResource);
                        // Same rule as the text-node branch above: a PART's property set inside a ControlTemplate lands at
                        // TEMPLATE priority, not the CLR setter's LOCAL - so a theme trigger/style can override it. Nested
                        // object values (e.g. <X.Template><ControlTemplate/>) used to skip this and go to Local.
                        if (CurrentTemplate != null && !propRef.IsAttachedProperty && typeInfo.ImplementsInterface("IAdamantiumComponent")
                            && HasAdamantiumProperty(typeInfo, propRef.Name))
                        {
                            var target = isRoot ? "this" : CurrentParent;
                            _writer.Add(Invoke(Variable(target), "SetValue", Unescaped(propRef.Name), nestedName, Priority("Template")));
                        }
                        else
                        {
                            _writer.Add(new AssignStep(symbolTarget, propRef.Name, nestedName));
                        }
                    }
                }
            }
        }

        void ProcessLogicalChildren(params AumlAstObjectNode[] childNodes)
        {
            for (var childIndex = 0; childIndex < childNodes.Length; childIndex++)
            {
                var child = childNodes[childIndex];

                var childStep = new ChildStep(child);
                _writer.Open(childStep);

                // x:Load: the child is not built here at all. What goes in its place is a slot holding the factory that
                // would build it, and the condition that says when - see EmitLoadSlot.
                if (GetLoadDirective(child) is { } load)
                {
                    childStep.Placement = ChildPlacement.LoadSlot;
                    EmitLoadSlot(child, load, childIndex, elementName, typeInfo, diagnostics, isResource);
                    _writer.Close();
                    continue;
                }

                var childName = ProcessControlElements(child, diagnostics, isResource);

                if (isRoot && Metadata.RootEntityType == EntityType.ResourceDictionary && IsScopeStyle(child))
                {
                    childStep.Placement = ChildPlacement.Resources;
                    _writer.Add(Invoke(new MemberValue(Variable("this"), "Styles"), "Add", childName));
                }
                else if (element.TypeReference.Name == "Style")
                {
                    childStep.Placement = ChildPlacement.Owner;
                    _writer.Add(Invoke(Variable(ParentName), "Add", childName));
                }
                else if (typeInfo.ImplementsInterface("ITrigger"))
                {
                    childStep.Placement = ChildPlacement.Owner;
                    _writer.Add(Invoke(Variable(ParentName), "Add", childName));
                }
                else if (MarkupCollectionFor(typeInfo, child) is { } collection)
                {
                    childStep.Placement = ChildPlacement.MarkupCollection;
                    _writer.Add(Invoke(new MemberValue(Variable(elementName), collection), "Add", childName));
                }
                // [Content] BEFORE IContainer: the attribute is a statement by THIS type about where its markup
                // children go and is searched most-derived first, while IContainer is one implementation for the whole
                // hierarchy - a type declaring its own content property and inheriting a container was losing the
                // declaration. Where both agree (a Panel's Children) the emitted line is the same either way.
                else if (typeInfo.FindPropertyWithAttribute("Adamantium.UI.Core.ContentAttribute", out var contentProp))
                {
                    childStep.Placement = ChildPlacement.Tree;
                    if (contentProp.PropertyType.IsCollection())
                    {
                        _writer.Add(Invoke(new MemberValue(Variable(elementName), contentProp.Name), "Add", childName));
                    }
                    else
                    {
                        _writer.Add(new AssignStep(Variable(elementName), contentProp.Name, childName));
                    }
                }
                else if (typeInfo.ImplementsInterface("IContainer"))
                {
                    childStep.Placement = ChildPlacement.Tree;
                    var container = new CastValue(Type(typeInfo.GetInterface("IContainer")), Variable(elementName));
                    _writer.Add(Invoke(container, "AddOrSetChildComponent", childName));
                }
                else if (isResource && Metadata.RootEntityType != EntityType.ResourceDictionary)
                {
                    childStep.Placement = ChildPlacement.Resources;
                    _writer.Add(Invoke(null, "Add", childName));
                }

                _writer.Close();
            }
        }

        Action emitBody = () =>
        {
            // A template's whole body (child tree + Triggers) is emitted into its builder by EmitTemplateBuilder above,
            // so there are no further properties/children to process here.
            if (isTemplate) return;
            ProcessProperties(properties.ToArray());
            ProcessLogicalChildren(children.ToArray());
        };

        if (isRoot)
        {
            emitBody();
        }
        else
        {
            WithElement(elementName, emitBody);
        }

        if (isResource && key != null)
        {
            _writer.Add(Invoke(null, "Add", key, Variable(elementName)));
        }

        PopTypeContext();
        _writer.Close();

        return Variable(elementName);
    }

    private IEnumerable<AumlAstPropertyNode> EachInItsPropertyStep(IEnumerable<AumlAstPropertyNode> propertyNodes)
    {
        foreach (var node in propertyNodes)
        {
            _writer.Open(new PropertyStep(node));
            yield return node;
            _writer.Close();
        }
    }

    /// <summary>The <c>x:Load</c> directive on this element, or null. Its value has already been judged by the
    /// transformer - text here is only ever True or False, anything else is a binding node.</summary>
    internal static AumlAstDirective GetLoadDirective(AumlAstObjectNode node) =>
        NodeScan.First<AumlAstDirective>(node.Children, static d => d.Name == AumlDirectives.Load);

    /// <summary>The field holding the slot of a NAMED held-back element. The name is exposed as a property that reads
    /// through it, so asking for the element by name builds it - see the generated accessor.</summary>
    internal static string LoadSlotField(string elementName) => $"_load_{elementName}";

    // An element held back by x:Load is not constructed here: what is emitted in its place is a LoadSlot holding a local
    // factory function (the element's whole build, moved wholesale into it) and the condition. The slot is a LOGICAL
    // child of the container, never a visual one - layout and rendering never see it - and it has to be in the tree at
    // all because a condition may be a BINDING, which resolves against the DataContext in force at the element's place.
    private void EmitLoadSlot(AumlAstObjectNode child, AumlAstDirective load, int index, string containerVar,
        IResolvedType containerType, IDiagnosticSink diagnostics, bool isResource)
    {
        // Not inside a template: one slot field would be shared by every templated control. The container must support
        // IContainer to reinsert the element in place.
        if (CurrentTemplate != null)
        {
            diagnostics.ReportError(Metadata.ClassName,
                MarkupMessages.LoadInsideTemplate(), load);
            return;
        }

        if (!containerType.ImplementsInterface("IContainer") || !containerType.ImplementsInterface("IFundamentalUIComponent"))
        {
            diagnostics.ReportError(Metadata.ClassName,
                MarkupMessages.LoadInsideContainer(containerType.Name), load);
            return;
        }

        // A named element's slot goes into the FIELD its name reads through, so asking for it by name reaches the same
        // slot and builds it. An unnamed one is only ever driven by its condition, so a local is enough.
        var isNamed = Metadata.NamedElementsMap.TryGetValue(child, out var childName_);
        var slotVar = isNamed ? LoadSlotField(childName_) : GenerateNextElementName("load");
        var slot = Variable(slotVar);
        var builder = $"Build_{GenerateNextElementName("load")}";

        _writer.BlankLine();
        _writer.Open(new FunctionStep(Type(UIComponentFqn), builder));
        var childName = ProcessControlElements(child, diagnostics, isResource);
        _writer.Add(new ReturnStep(childName));
        _writer.Close();

        _writer.Add(new DeclareStep(slotVar,
            new NewValue(Type(LoadSlotFqn), Variable(builder), new CastValue(Type(ContainerFqn), Variable(containerVar)),
                new LiteralValue(index.ToString())),
            isNamed));
        _writer.Add(Invoke(new CastValue(Type(FundamentalComponentFqn), Variable(containerVar)), "AddLogicalChild", slot));

        switch (load.Value)
        {
            case AumlAstTextNode { Text: var text } when text.Trim() == "True":
                _writer.Add(new AssignStep(slot, "Condition", new LiteralValue("true")));
                break;
            case AumlAstTextNode:
                // "False" - nothing is built until something asks for it by name.
                break;
            default:
                _writer.Add(Invoke(slot, "SetBinding", new MemberValue(Type(LoadSlotFqn), "ConditionProperty"),
                    EmitBinding(load.Value, diagnostics, isResource)));
                break;
        }

        _writer.BlankLine();
    }

    /// <summary>Whether this value is marked <c>x:Shared="False"</c> - build it per target, do not share one instance.</summary>
    private static bool IsPerTarget(IAumlAstValueNode value)
    {
        if (value is not AumlAstObjectNode obj)
        {
            return false;
        }

        foreach (var child in obj.Children)
        {
            if (child is AumlAstDirective { Name: AumlDirectives.Shared } directive
                && directive.Value is AumlAstTextNode text
                && string.Equals(text.Text?.Trim(), "False", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPerTargetType(IAumlAstValueNode value)
    {
        if (value is not AumlAstObjectNode obj)
        {
            return false;
        }

        var container = Metadata.TypeResolver.GetResolvedAssembly(obj.TypeReference.Assembly);
        var type = container?.GetTypeByShortName(obj.TypeReference.Name);
        return type != null && type.HasAttribute("Adamantium.UI.Core.Resources.PerTargetAttribute");
    }

    /// <summary>Emits a builder for a per-target value and returns the expression that wraps it. Same shape as a
    /// template's builder (see <see cref="EmitTemplateBuilder"/>): a local function that constructs the tree, handed to
    /// the value object so the setter can call it once per element it matches.</summary>
    private BuildValue EmitPerTargetValue(IAumlAstValueNode value, bool isResource, IDiagnosticSink diagnostics)
    {
        var builder = $"Build_{GenerateNextElementName("shared")}";

        _writer.BlankLine();
        _writer.Open(new FunctionStep(Type("object"), builder));
        var built = ProcessNestedValue(value, diagnostics, isResource);
        _writer.Add(new ReturnStep(built));
        _writer.Close();
        _writer.BlankLine();

        return new NewValue(Type(Metadata.DefaultTypeContainer.PerTargetValue), Variable(builder));
    }

    // Emits `target = new Template(Build_xxx)` and the local builder returning a TemplateResult. Templates carry content
    // only through this builder, whether a property value or a keyed resource.
    private void EmitTemplateBuilder(string templateVar, bool isField, IResolvedType templateType, IAumlAstNode templateNode,
        bool isResource, IDiagnosticSink diagnostics)
    {
        var templateBuilderMethod = $"Build_{GenerateNextElementName("template")}";
        var template = Variable(templateVar);

        _writer.Add(new DeclareStep(templateVar, new NewValue(Type(templateType), Variable(templateBuilderMethod)), isField));

        // The builder below produces the template's VISUAL (a DataTemplate's item / a HierarchicalDataTemplate's header).
        // A template's own CONFIGURATION attributes (e.g. HierarchicalDataTemplate.ItemsSource / ItemContainerStyle /
        // ItemTemplate) aren't part of that visual - emit them onto the template variable itself.
        EmitTemplateConfigProperties(template, templateNode, isResource, diagnostics);
        if (DataTypeOf(templateNode) is { } dataType)
        {
            _writer.Add(new AssignStep(template, "DataType", new TypeOfValue(Type(dataType))));
        }

        var templateResult = Type(Metadata.DefaultTypeContainer.TemplateResult);
        var result = Variable("result");

        _writer.BlankLine();
        _writer.Open(new FunctionStep(templateResult, templateBuilderMethod));

        _writer.Add(new DeclareStep(result.Name, new NewValue(templateResult)));

        PushTemplate(result.Name);

        var child = templateNode.GetLogicalChildrenObjects().FirstOrDefault();
        var childName = ProcessControlElements(child, diagnostics, false);
        _writer.Add(new AssignStep(result, "RootComponent", childName));

        var templateProperties = templateNode.GetProperties().ToList();
        WithElement(result.Name, () => // Temporary Context for object "result"
        {
            PushTypeContext(Metadata.DefaultTypeContainer.ControlTemplate);
            foreach (var templateProp in templateProperties)
            {
                var templatePropRef = (AumlAstPropertyReference)templateProp.Property;
                if (templatePropRef.Name == "Triggers")
                {
                    foreach (var triggerValueNode in templateProp.Values)
                    {
                        if (triggerValueNode is AumlAstObjectNode triggerObjectNode)
                        {
                            var triggerVariableName = ProcessControlElements(triggerObjectNode, diagnostics, isResource);
                            _writer.Add(Invoke(new MemberValue(result, "Triggers"), "Add", triggerVariableName));
                        }
                    }
                }
            }
            PopTypeContext();
        });

        PopTemplate();

        _writer.Add(new ReturnStep(result));
        _writer.Close();
    }

    private IResolvedType DataTypeOf(IAumlAstNode templateNode)
    {
        if (templateNode is not AumlAstObjectNode template
            || Metadata.TypeResolver.Resolve(template.TypeReference.GetFullTypeName()) is not { } templateType
            || !templateType.IsAssignableTo(DataTemplateTypeName))
        {
            return null;
        }

        var directive = template.Children.OfType<AumlAstDirective>().FirstOrDefault(d => d.Name == AumlDirectives.DataType);
        return directive?.Value is AumlAstTypeReferenceValueNode { TypeReference: { IsResolved: true } type }
            ? Metadata.TypeResolver.Resolve(type.GetFullTypeName())
            : null;
    }

    private IResolvedType TypeNamedBy(IAumlAstNode templateNode, AumlAstPropertyNode prop)
    {
        var templateType = (templateNode as AumlAstObjectNode)?.TypeReference is { } reference
            ? Metadata.TypeResolver.Resolve(reference.GetFullTypeName())
            : null;
        var name = ((AumlAstPropertyReference)prop.Property).Name;
        return templateType?.GetMemberByName(name)?.MemberType?.FullName == "System.Type"
            ? Metadata.TypeResolver.Resolve(prop.GetFullTypeValue())
            : null;
    }

    // A template's own configuration (e.g. ItemsSource as a binding object, ItemContainerStyle, TargetType). Other text
    // values are generation-time hints and skipped.
    private void EmitTemplateConfigProperties(VariableValue template, IAumlAstNode templateNode, bool isResource, IDiagnosticSink diagnostics)
    {
        foreach (var prop in templateNode.GetProperties())
        {
            var name = ((AumlAstPropertyReference)prop.Property).Name;
            if (name == "Triggers") continue;
            switch (prop.Values.FirstOrDefault())
            {
                case not null when TypeNamedBy(templateNode, prop) is { } type:
                    _writer.Add(new AssignStep(template, name, new TypeOfValue(Type(type))));
                    break;
                case AumlAstMarkupExtensionNode me when me.TypeReference?.Name is "Binding" or "MultiBinding":
                    _writer.Add(new AssignStep(template, name, EmitBinding(me, diagnostics, isResource)));
                    break;
                case AumlAstObjectNode obj:
                    _writer.Add(new AssignStep(template, name, ProcessControlElements(obj, diagnostics, isResource)));
                    break;
            }
        }
    }

    private void ReportIfUnparsable(IResolvedType type, IDiagnosticSink diagnostics, string where)
    {
        if (!TypeParserCheck.CanParse(type))
        {
            diagnostics.ReportWarning(Metadata.ClassName, MarkupMessages.NoTypeParser(where, type.FullName));
        }
    }

    // Enum flag lists split on ',' or '|', matching TypeCastFactory.ParseEnum; not gated on [Flags], so misuse reports a
    // clear member error.
    private static readonly char[] EnumFlagSeparators = [',', '|'];

    private static EnumValue BuildEnumExpression(string valueText, IResolvedType member)
    {
        var type = Type(member);
        if (valueText.IndexOfAny(EnumFlagSeparators) < 0)
        {
            return new EnumValue(type, [valueText]);
        }

        var parts = valueText.Split(EnumFlagSeparators);
        var terms = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var name = part.Trim();
            if (name.Length == 0) continue;

            terms.Add(name);
        }

        return new EnumValue(type, terms.Count == 0 ? [valueText] : terms);
    }

    // The value of a literal attribute, typed as the property's type (so it can be assigned directly OR boxed for a
    // priority-aware SetValue without losing its type, e.g. Opacity="0" must be (double)0, not a boxed int).
    private BuildValue BuildValueExpression(string valueText, IResolvedType member, IDiagnosticSink diagnostics, string where)
    {
        if (member.TypeKind == ResolvedTypeKind.Enum)
            return BuildEnumExpression(valueText, member);

        // Floating "specials" are not valid C# numeric literals - emit the framework constant instead (mirrors the
        // runtime TypeCastFactory, which maps "Auto" -> NaN). Lets a markup double carry them, e.g. an animation's
        // IterationCount="Infinity" (loop forever) or a Width="NaN".
        if (member.SpecialType is ResolvedSpecialType.System_Double or ResolvedSpecialType.System_Single)
        {
            var floatType = Type(member.SpecialType == ResolvedSpecialType.System_Double ? "double" : "float");
            switch (valueText)
            {
                case "Infinity":
                case "+Infinity": return new MemberValue(floatType, "PositiveInfinity");
                case "-Infinity": return new MemberValue(floatType, "NegativeInfinity");
                case "NaN":
                case "Auto": return new MemberValue(floatType, "NaN");
            }
        }

        switch (member.SpecialType)
        {
            case ResolvedSpecialType.System_String:
                return new StringValue(valueText);
            case ResolvedSpecialType.System_Double:
            case ResolvedSpecialType.System_Single:
            case ResolvedSpecialType.System_Decimal:
            case ResolvedSpecialType.System_SByte:
            case ResolvedSpecialType.System_Byte:
            case ResolvedSpecialType.System_Int16:
            case ResolvedSpecialType.System_Int32:
            case ResolvedSpecialType.System_Int64:
            case ResolvedSpecialType.System_UInt16:
            case ResolvedSpecialType.System_UInt32:
            case ResolvedSpecialType.System_UInt64:
                return new LiteralValue(valueText);
            case ResolvedSpecialType.System_Boolean:
                return new LiteralValue(valueText.ToLowerInvariant());
            case ResolvedSpecialType.System_Object:
                return new StringValue(valueText);
            default:
                ReportIfUnparsable(member, diagnostics, where);
                return ParseCall(member, valueText);
        }
    }

    private CallValue ParseCall(IResolvedType type, string text) =>
        new(Type(Metadata.DefaultTypeContainer.TypeParser), "Parse", [Type(type)], new StringValue(text));

    private const string MarkupItemAttributeName = "Adamantium.UI.Core.MarkupItemAttribute";

    private const string DataTemplateTypeName = "Adamantium.UI.Core.Templates.DataTemplate";

    private IResolvedType ResolveTypeArgument(IAumlAstValueNode value) =>
        value is AumlAstTypeReferenceValueNode typeValue
            ? Metadata.TypeResolver.Resolve(typeValue.TypeReference.GetFullTypeName())
            : Metadata.TypeResolver.ResolveByShortName(value.GetTextValue());

    // A child element that is not a visual - <Grid><ColumnDefinition/></Grid> - belongs to the parent's [MarkupItem]
    // collection that accepts it. Returns that property's name, or null when the child is an ordinary tree child.
    private string MarkupCollectionFor(IResolvedType parentType, AumlAstObjectNode child)
    {
        var childType = Metadata.TypeResolver.Resolve(child.TypeReference.GetFullTypeName());
        if (parentType == null || childType == null || childType.ImplementsInterface("IUIComponent")) return null;

        foreach (var property in parentType.GetAllProperties())
        {
            var markupItem = property.PropertyType?.GetAttribute(MarkupItemAttributeName);
            if (markupItem == null) continue;
            if (!markupItem.NamedArguments.TryGetValue("ItemType", out var itemType) || itemType == null) continue;

            if (childType.IsAssignableTo(itemType.ToString())) return property.Name;
        }

        return null;
    }

    // Records an element's source position so the designer can map clicks in nested views back here; debug builds only.
    private void EmitDesignSource(string elementName, AumlAstObjectNode element)
    {
        if (string.IsNullOrEmpty(Metadata.SourceFilePath)
            || EntityType is EntityType.ResourceDictionary or EntityType.StyleSet or EntityType.Theme or EntityType.ThemeVariant)
        {
            return;
        }

        _writer.Open(new ConditionalStep("DEBUG"));
        _writer.Add(Invoke(Type(DesignFqn), "Source", Variable(elementName), new StringValue(Metadata.SourceFilePath),
            new LiteralValue(element.Line.ToString()), new LiteralValue(element.Position.ToString())));
        _writer.Close();
    }

    private BuildValue ProcessNestedValue(IAumlAstNode value, IDiagnosticSink diagnostics, bool isResource)
    {
        if (value is AumlAstObjectNode objNode)
        {
            return ProcessControlElements(objNode, diagnostics, isResource);
        }

        // {x:Static Type.Member} - the member is read where the value is used, so it needs no variable of its own.
        if (value is AumlAstStaticMemberValueNode staticMember)
        {
            return new MemberValue(Type(Qualified(staticMember.TypeReference.GetFullTypeName())), staticMember.MemberName);
        }

        if (value is AumlAstMarkupExtensionLiteral literal)
        {
            var resolvedAssembly = Metadata.TypeResolver.ResolveAssembly(literal.TypeReference.Assembly);
            var type = resolvedAssembly.Types.First(x => x.Name == literal.TypeReference.Name);
            var name = GenerateNextElementName();
            _writer.Add(new DeclareStep(name, BuildValueExpression(literal.Text, type, diagnostics,
                $"{literal.TypeReference.Name} \"{literal.Text}\"")));
            return Variable(name);
        }

        if (value is AumlAstMarkupExtensionNode extension)
        {
            var type = Metadata.TypeResolver.ResolveAssembly(extension.TypeReference.Assembly)
                .Types.First(x => x.Name == extension.TypeReference.Name);
            var name = GenerateNextElementName();
            var created = Variable(name);
            _writer.Add(new DeclareStep(name, new NewValue(Type(type))));

            foreach (var arg in extension.Arguments)
            {
                if (arg.Value is AumlAstTypeReferenceValueNode typeValueNode)
                {
                    var fullTypeName = typeValueNode.TypeReference.GetFullTypeName();
                    _writer.Add(new AssignStep(created, arg.Name, new TypeOfValue(Type(Qualified(fullTypeName)))));
                }
                else if (arg.Value is IAumlAstMarkupExtensionLiteral or IAumlAstMarkupExtensionNode
                         || arg.Value is AumlAstObjectNode or AumlAstStaticMemberValueNode)
                {
                    var nested = ProcessNestedValue(arg.Value, diagnostics, isResource);
                    _writer.Add(new AssignStep(created, arg.Name, nested));
                }
                else
                {
                    var resolvedMember = Metadata.TypeResolver.ResolveAssembly(arg.Value.TypeReference.Assembly).Types
                        .First(x => x.Name == arg.Value.TypeReference.Name);
                    _writer.Add(new AssignStep(created, arg.Name, BuildValueExpression(arg.Value.GetTextValue(), resolvedMember,
                        diagnostics, $"{extension.TypeReference.Name} {arg.Name}=\"{arg.Value.GetTextValue()}\"")));
                }
            }

            return created;
        }

        return NullValue.Instance;
    }

    // ---- bindings -------------------------------------------------------------------------------------------------

    // Emit the SetBinding call. An ATTACHED property (ToolTipService.ToolTip, Grid.Row, ...) is NOT registered on the
    // target's type, so binding by NAME (SetBinding("ToolTip", ...)) can't resolve it and throws. Bind to the property
    // OBJECT instead - <Owner>.<Name>Property - exactly as WPF binds to the DependencyProperty; the target type is
    // irrelevant, the property object fully identifies it.
    private static InvokeStep EmitSetBinding(string bindingTarget, AumlAstPropertyReference propRef, BuildValue binding)
    {
        if (propRef.IsAttachedProperty)
            return Invoke(Variable(bindingTarget), "SetBinding", new MemberValue(OwnerOf(propRef), $"{propRef.Name}Property"), binding);
        return Invoke(Variable(bindingTarget), "SetBinding", Unescaped(propRef.Name), binding);
    }

    private static string Qualified(string fullTypeName) => $"global::{fullTypeName}";

    private static TypeValue Type(string qualifiedName) => new(qualifiedName);

    private static TypeValue Type(IResolvedType type) => new(type.QualifiedName, type);

    private static VariableValue Variable(string name) => new(name);

    private static StringValue Unescaped(string text) => new(text, escaped: false);

    private static TypeValue OwnerOf(AumlAstPropertyReference propRef) => Type(Qualified(propRef.OwnerType.GetFullTypeName()));

    private static MemberValue Priority(string priority) => new(Type(ValuePriorityFqn), priority);

    private static InvokeStep Invoke(BuildValue target, string method, params BuildValue[] arguments) =>
        new(new CallValue(target, method, arguments));

    private const string LoadSlotFqn = "global::Adamantium.UI.Controls.LoadSlot";
    private const string ContainerFqn = "global::Adamantium.UI.Core.IContainer";
    private const string FundamentalComponentFqn = "global::Adamantium.UI.Core.IFundamentalUIComponent";
    private const string UIComponentFqn = "global::Adamantium.UI.Core.IUIComponent";
    private const string BindingFqn = "global::Adamantium.UI.Core.Data.Binding";
    private const string MultiBindingFqn = "global::Adamantium.UI.Core.Data.MultiBinding";
    private const string PropertyPathFqn = "global::Adamantium.UI.Core.PropertyPath";
    private const string BindingModeFqn = "global::Adamantium.UI.Core.Data.BindingMode";
    private const string UpdateSourceTriggerFqn = "global::Adamantium.UI.Core.Data.UpdateSourceTrigger";
    private const string ValueConverterFqn = "global::Adamantium.UI.Core.Data.IValueConverter";
    private const string MultiValueConverterFqn = "global::Adamantium.UI.Core.Data.IMultiValueConverter";
    private const string LocalizeFqn = "global::Adamantium.UI.Core.Localization.Localize";
    private const string ResourceResolverFqn = "global::Adamantium.UI.Core.Resources.ResourceResolver";
    private const string NameScopeFqn = "global::Adamantium.UI.Core.Templates.NameScope";
    private const string ValuePriorityFqn = "global::Adamantium.UI.Core.ValuePriority";
    private const string DesignFqn = "global::Adamantium.UI.Core.Design";

    private bool IsScopeStyle(AumlAstObjectNode node) =>
        KeyOf(node) == null && Metadata.DefaultTypeContainer.Style is { } style
        && Metadata.TypeResolver.GetResolvedAssembly(node.TypeReference.Assembly)?.GetTypeByShortName(node.TypeReference.Name) is { } type
        && (type.FullName == style.FullName || type.InheritsFrom(style.FullName));

    // The x:Key directive of an inline object node (an inline resource entry), or null if it has none.
    // x:Key as written: text, or {x:Static Type.Member} - a constant shared with code, so a key is never typed twice.
    // Null when there is none.
    private static BuildValue KeyOf(IAumlAstNode node)
    {
        if (node is not AumlAstObjectNode obj) return null;

        foreach (var child in obj.Children)
        {
            if (child is not AumlAstDirective { Name: AumlDirectives.Key } directive) continue;

            return directive.Value switch
            {
                AumlAstTextNode { Text.Length: > 0 } text => Unescaped(text.Text),
                AumlAstStaticMemberValueNode member =>
                    new MemberValue(Type(Qualified(member.TypeReference.GetFullTypeName())), member.MemberName),
                _ => null
            };
        }

        return null;
    }

    // A Binding/MultiBinding written as a markup extension ({Binding}/{MultiBinding}) or as an element
    // (<Binding/>, <MultiBinding>...</MultiBinding>). The codegen treats these by NAME (like ResourceReference).
    private static bool IsBindingNode(IAumlAstNode node) => node switch
    {
        AumlAstMarkupExtensionNode me => me.TypeReference?.Name is "Binding" or "MultiBinding",
        AumlAstObjectNode obj => obj.TypeReference?.Name is "Binding" or "MultiBinding",
        _ => false,
    };

    // True when a property is typed to HOLD a binding object itself (Binding/MultiBinding/BindingBase) - e.g.
    // DataTrigger.Binding. Such a property takes the {Binding} value by direct assignment; an ordinary property instead
    // gets a live binding established on it via SetBinding.
    private static bool IsBindingTypedProperty(IResolvedType type) =>
        type?.FullName is "Adamantium.UI.Core.Data.Binding"
                       or "Adamantium.UI.Core.Data.MultiBinding"
                       or "Adamantium.UI.Core.Data.BindingBase";

    // A property typed BindingBase holds any binding - a {Localize} too; one typed Binding or MultiBinding holds only that.
    private static bool HoldsAnyBinding(IResolvedType type) => type?.FullName == "Adamantium.UI.Core.Data.BindingBase";

    private static string PropertyPath(AumlAstPropertyReference propRef) =>
        propRef.IsAttachedProperty ? $"{propRef.OwnerType.Name}.{propRef.Name}" : propRef.Name;

    private static bool IsPlainProperty(IResolvedType type, AumlAstPropertyReference propRef) =>
        type != null && !propRef.IsAttachedProperty && !HasAdamantiumProperty(type, propRef.Name);

    private void ReportLiveResourceOnPlainProperty(IDiagnosticSink diagnostics, string extension, string key,
        IResolvedType type, AumlAstPropertyReference propRef) =>
        diagnostics.ReportError(Metadata.ClassName,
            MarkupMessages.LiveResourceOnPlainProperty(extension, key, type.Name, propRef.Name), propRef);

    private static bool HasAdamantiumProperty(IResolvedType type, string property)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.GetMemberByName(property + "Property") is { IsStatic: true })
            {
                return true;
            }
        }

        return false;
    }

    // Emits the steps that construct a Binding/MultiBinding and returns the variable holding it. Recurses for nested
    // multi-bindings, so multibinding-inside-multibinding generates correctly.
    private BuildValue EmitBinding(IAumlAstNode node, IDiagnosticSink diagnostics, bool isResource) => node switch
    {
        AumlAstMarkupExtensionNode me when me.TypeReference?.Name == "MultiBinding"
            => EmitMultiBindingFromMarkup(me, diagnostics, isResource),
        AumlAstMarkupExtensionNode me => EmitBindingFromMarkup(me, diagnostics, isResource),
        AumlAstObjectNode obj when obj.TypeReference?.Name == "MultiBinding"
            => EmitMultiBindingFromObject(obj, diagnostics, isResource),
        AumlAstObjectNode obj => EmitBindingFromObject(obj, diagnostics, isResource),
        _ => NullValue.Instance,
    };

    // {Localize Table.Key, name=value}: the key is checked by the compiler through nameof, the placeholders by the
    // transformer; an argument is a binding to follow, a property of the templated control, another phrase, or a
    // plain value.
    private BuildValue EmitLocalizedBinding(AumlAstLocalizedStringNode localized, IDiagnosticSink diagnostics, bool isResource)
    {
        var name = GenerateNextElementName("localized");
        var localize = Variable(name);
        if (localized.TableSource != null)
        {
            _writer.Add(new DeclareStep(name, new NewValue(Type(LocalizeFqn), NullValue.Instance, NullValue.Instance)));
            _writer.Add(new AssignStep(localize, "TableSource", Followed(localized.TableSource)));
        }
        else
        {
            var table = Type(Qualified(localized.TableFullName));
            BuildValue key = localized.KeySource == null
                ? new NameOfValue(new MemberValue(table, localized.Key))
                : NullValue.Instance;
            _writer.Add(new DeclareStep(name, new NewValue(Type(LocalizeFqn), new MemberValue(table, "Current"), key)));
        }

        if (localized.KeySource != null)
        {
            _writer.Add(new AssignStep(localize, "KeySource", Followed(localized.KeySource)));
        }

        foreach (var argument in localized.Arguments)
        {
            _writer.Add(new IndexAssignStep(new MemberValue(localize, "Arguments"), new StringValue(argument.Name),
                Followed(argument.Value)));
        }

        return localize;

        BuildValue Followed(IAumlAstValueNode value) => value switch
        {
            _ when IsBindingNode(value) => EmitBinding(value, diagnostics, isResource),
            AumlAstMarkupExtensionNode { TypeReference.Name: "TemplateBinding" } templateBinding =>
                CurrentTemplate == null && !isResource
                    ? ReportOutsideTemplate(diagnostics, templateBinding)
                    : EmitTemplateBinding(templateBinding),
            AumlAstLocalizedStringNode phrase => EmitLocalizedBinding(phrase, diagnostics, isResource),
            _ => new StringValue(value.GetTextValue() ?? string.Empty),
        };
    }

    private BuildValue ReportOutsideTemplate(IDiagnosticSink diagnostics, IAumlLineInfo at)
    {
        diagnostics.ReportError(Metadata.ClassName, MarkupMessages.TemplateBindingOutsideTemplate(), at);
        return NullValue.Instance;
    }

    private BuildValue EmitTemplateBinding(AumlAstMarkupExtensionNode extension)
    {
        var name = GenerateNextElementName("tb");
        var templateBinding = Variable(name);
        _writer.Add(new DeclareStep(name, new NewValue(Type(Qualified(extension.TypeReference.GetFullTypeName())))));

        foreach (var argument in extension.Arguments)
        {
            if (string.IsNullOrEmpty(argument.Name))
            {
                _writer.Add(new AssignStep(templateBinding, "Path", Unescaped(argument.Value.GetTextValue())));
            }
            else
            {
                _writer.Add(new AssignStep(templateBinding, argument.Name,
                    new MemberValue(Type(Qualified(argument.Value.TypeReference.GetFullTypeName())), argument.Value.GetTextValue())));
            }
        }

        return templateBinding;
    }

    private BuildValue EmitBindingFromMarkup(AumlAstMarkupExtensionNode me, IDiagnosticSink diagnostics, bool isResource)
    {
        var name = GenerateNextElementName("binding");
        _writer.Add(new DeclareStep(name, new NewValue(Type(BindingFqn))));
        foreach (var arg in me.Arguments)
            EmitBindingProperty(name, string.IsNullOrEmpty(arg.Name) ? "Path" : arg.Name, arg.Value, isMulti: false, diagnostics, isResource);
        return Variable(name);
    }

    private BuildValue EmitBindingFromObject(AumlAstObjectNode obj, IDiagnosticSink diagnostics, bool isResource)
    {
        var name = GenerateNextElementName("binding");
        _writer.Add(new DeclareStep(name, new NewValue(Type(BindingFqn))));
        foreach (var child in obj.Children)
            if (child is AumlAstPropertyNode { Property: AumlAstPropertyReference pref } pn && pn.Values.Count > 0)
                EmitBindingProperty(name, pref.Name, pn.Values[0], isMulti: false, diagnostics, isResource);
        return Variable(name);
    }

    private BuildValue EmitMultiBindingFromMarkup(AumlAstMarkupExtensionNode me, IDiagnosticSink diagnostics, bool isResource)
    {
        var name = GenerateNextElementName("multiBinding");
        var bindings = new MemberValue(Variable(name), "Bindings");
        _writer.Add(new DeclareStep(name, new NewValue(Type(MultiBindingFqn))));
        foreach (var arg in me.Arguments)
        {
            if (string.IsNullOrEmpty(arg.Name))                       // positional = a child binding
            {
                if (IsBindingNode(arg.Value))
                {
                    _writer.Add(Invoke(bindings, "Add", EmitBinding(arg.Value, diagnostics, isResource)));
                }
            }
            else EmitBindingProperty(name, arg.Name, arg.Value, isMulti: true, diagnostics, isResource);
        }
        return Variable(name);
    }

    private BuildValue EmitMultiBindingFromObject(AumlAstObjectNode obj, IDiagnosticSink diagnostics, bool isResource)
    {
        var name = GenerateNextElementName("multiBinding");
        var bindings = new MemberValue(Variable(name), "Bindings");
        _writer.Add(new DeclareStep(name, new NewValue(Type(MultiBindingFqn))));
        foreach (var child in obj.Children)
        {
            switch (child)
            {
                case AumlAstObjectNode childObj when IsBindingNode(childObj):
                    _writer.Add(Invoke(bindings, "Add", EmitBinding(childObj, diagnostics, isResource)));
                    break;
                case AumlAstPropertyNode { Property: AumlAstPropertyReference pref } pn when pn.Values.Count > 0:
                    if (pref.Name == "Bindings")
                    {
                        foreach (var v in pn.Values)
                        {
                            if (IsBindingNode(v))
                            {
                                _writer.Add(Invoke(bindings, "Add", EmitBinding(v, diagnostics, isResource)));
                            }
                        }
                    }
                    else EmitBindingProperty(name, pref.Name, pn.Values[0], isMulti: true, diagnostics, isResource);
                    break;
            }
        }
        return Variable(name);
    }

    private void EmitBindingProperty(string bindingVar, string name, IAumlAstValueNode value, bool isMulti,
        IDiagnosticSink diagnostics, bool isResource)
    {
        var binding = Variable(bindingVar);
        switch (name)
        {
            case "Path":
                if ((value as AumlAstTextNode)?.Text?.Trim() is { Length: > 0 } path)
                {
                    _writer.Add(new AssignStep(binding, "Path", new NewValue(Type(PropertyPathFqn), new StringValue(path))));
                }

                break;
            case "Mode":
                if ((value as AumlAstTextNode)?.Text is { Length: > 0 } mode)
                {
                    _writer.Add(new AssignStep(binding, "Mode", new MemberValue(Type(BindingModeFqn), mode)));
                }

                break;
            case "UpdateSourceTrigger" when !isMulti:
                if ((value as AumlAstTextNode)?.Text?.Trim() is { Length: > 0 } trigger)
                {
                    _writer.Add(new AssignStep(binding, "UpdateSourceTrigger",
                        new MemberValue(Type(UpdateSourceTriggerFqn), trigger)));
                }

                break;
            case "Converter":
                var converter = EmitValueExpression(value, isMulti ? MultiValueConverterFqn : ValueConverterFqn, diagnostics, isResource);
                if (converter != null)
                {
                    _writer.Add(new AssignStep(binding, "Converter", converter));
                }

                break;
            case "ConverterParameter":
                var parameter = EmitValueExpression(value, "object", diagnostics, isResource);
                if (parameter != null)
                {
                    _writer.Add(new AssignStep(binding, "ConverterParameter", parameter));
                }

                break;
            case "StringFormat":
                if ((value as AumlAstTextNode)?.Text is { } format)
                {
                    _writer.Add(new AssignStep(binding, "StringFormat", new StringValue(format)));
                }

                break;
            case "Source":
                var source = EmitValueExpression(value, "object", diagnostics, isResource);
                if (source != null)
                {
                    _writer.Add(new AssignStep(binding, "Source", source));
                }

                break;
            case "ElementName":
                if ((value as AumlAstTextNode)?.Text?.Trim() is { Length: > 0 } elementName)
                {
                    _writer.Add(new AssignStep(binding, "ElementName", Unescaped(elementName)));
                }

                break;
            default:
                diagnostics.ReportWarning(Metadata.ClassName, MarkupMessages.UnknownBindingProperty(name), value);
                break;
        }
    }

    // A binding sub-value (Converter / ConverterParameter / Source): a {ResourceReference}, an inline element, a
    // converter authored as a markup extension ({local:MyConverter} -> new MyConverter()), or a literal.
    private BuildValue EmitValueExpression(IAumlAstValueNode value, string targetTypeFqn, IDiagnosticSink diagnostics, bool isResource)
    {
        switch (value)
        {
            case AumlAstMarkupExtensionNode me when me.TypeReference?.Name?.StartsWith("ResourceReference") == true:
                var key = (me.Arguments.FirstOrDefault()?.Value as AumlAstTextNode)?.Text;
                return string.IsNullOrEmpty(key)
                    ? null
                    : new CallValue(Type(ResourceResolverFqn), "Resolve", [Type(targetTypeFqn)], Unescaped(key));
            case AumlAstMarkupExtensionNode me:
                return ProcessNestedValue(me, diagnostics, isResource);          // converter authored as a markup extension
            case AumlAstObjectNode obj:
                return ProcessControlElements(obj, diagnostics, isResource);     // inline element
            case AumlAstTextNode text:
                return Unescaped(text.Text);
            default:
                return null;
        }
    }

    private class StubResolvedMember : IResolvedMember
    {
        public string Name { get; }
        public string DummyValue { get; }

        public StubResolvedMember(string name, string value)
        {
            Name = name;
            DummyValue = value;
        }

        public string FullName => Name;

        public bool HasAttribute(string attributeMetadataName)
        {
            return false;
        }

        public IResolvedAttribute GetAttribute(string attributeMetadataName)
        {
            return null;
        }

        public bool HasSetter()
        {
            return true;
        }

        public ResolvedMemberKind MemberKind => ResolvedMemberKind.Property;
        public object ConstantValue => null;
        public bool IsStatic => false;
        public bool IsPublic => true;
        public IReadOnlyList<string> ParameterNames => [];
        public IResolvedType MemberType => new StubResolvedType();
        public IResolvedType DeclaringType => new StubResolvedType();
    }

    private class StubResolvedType : IResolvedType
    {
        public string Name => "string";
        public string Namespace { get; }
        public string FullName => "System.String";
        public string QualifiedName => "global::System.String";
        public string AssemblyName => "System";
        public bool IsNamedType => false;
        public bool IsGenericType => false;

        public bool IsCreatable => true;
        public bool InheritsFrom(string baseTypeName) => false;

        public bool HasAttribute(string attributeName) => false;

        public IResolvedAttribute GetAttribute(string fullName) => null;

        public IEnumerable<IResolvedAttribute> GetAttributes() => [];

        public EntityType EntityType { get; }
        public IEnumerable<IResolvedType> TypeArguments { get; }
        public IResolvedType BaseType { get; }
        public IEnumerable<IResolvedMember> Members { get; }
        public IResolvedMember GetMemberByName(string memberName) => null;

        public IReadOnlyList<IResolvedProperty> GetAllProperties() => [];

        public bool IsAssignableTo(string fullName) => false;

        public bool ImplementsInterface(string name) => false;
        public IResolvedType GetInterface(string interfaceName) => null;

        public bool InheritsFromMarkupExtension(string fullyQualifiedName) => false;

        public bool FindPropertyWithAttribute(string attributeFullName, out IResolvedProperty property)
        {
            property = null;
            return false;
        }

        public bool IsAssignableFrom(IResolvedType other) => false;

        public bool IsCollection()
        {
            return false;
        }

        public ResolvedSpecialType SpecialType => ResolvedSpecialType.System_String;
        public ResolvedTypeKind TypeKind => ResolvedTypeKind.Class;
        public ResolvedMemberKind MemberKind => ResolvedMemberKind.Property;
    }
}
