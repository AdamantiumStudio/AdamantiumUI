using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Resources;

public class Setter : ISetter, IEquatable<Setter>
{
    public Setter()
    {
    }

    public Setter(string property, object value)
    {
        Property = property;
        Value = value;
    }

    public string Property { get; set; }
    public Object Value { get; set; }

    public string TargetName { get; set; }

    public int DeclarationOrder { get; set; }

    public int StyleBand { get; set; }

    /// <summary>Gives <paramref name="component"/> this setter's value as <paramref name="style"/>'s contribution. A
    /// component without the property is passed over: a style matched by class reaches elements of any type.</summary>
    public void Apply(IFundamentalUIComponent component, Style style, ITheme theme)
    {
        var property = AdamantiumPropertyMap.ResolveProperty(component.GetType(), Property);
        if (property == null)
        {
            return;
        }

        switch (Value)
        {
            case BindingBase binding:
                Connect(((BindingBase)binding.Clone()).CreateExpression(component, property), style);
                break;
            case ResourceReference resourceReference:
                ApplyResourceReference(component, style, theme, resourceReference);
                break;
            // The STYLE is the token: a live resource connection belongs to the style that made it. Without that, two
            // styles writing the same property at the same priority - which is exactly what a theme swap has, for the
            // few frames both are attached - share one slot, and whichever leaves last tears down the connection the
            // other established.
            case ThemeResource themeResource:
                themeResource.Apply(component, Property, SlotFor(property, style), style);
                break;
            case ObservableResource observableResource:
                observableResource.Apply(component, Property, SlotFor(property, style), style);
                break;
            case Ancestor ancestor:
                Connect(ancestor.CreateExpression(component, property), style);
                break;
            case Self self:
                Connect(self.CreateExpression(component, property), style);
                break;

            // x:Shared="False": build this element its OWN value. Everything else in this switch hands out one object to
            // every target, which is right for a brush and wrong for anything that belongs to the element it sits on.
            case PerTargetValue perTarget:
                component.SetStyleValue(property, perTarget.Create(), style);
                break;
            default:
                component.SetStyleValue(property, TypeCastFactory.CastFromString(Value, property.PropertyType), style);
                break;
        }
    }

    private static void Connect(BindingExpressionBase expression, Style style)
    {
        expression.OwnerStyle = style;
        BindingEngine.RegisterOwned(expression, style);
    }

    private static ValuePriority SlotFor(AdamantiumProperty property, Style style)
    {
        if (style is not { IsTypeDefault: true }) return ValuePriority.Style;

        return property.CanInherit ? ValuePriority.TypeDefault : ValuePriority.Style;
    }

    // Resolves tree-scoped; a Local miss before the element has ancestors is deferred to attach. Theme/Global resolve
    // immediately.
    private void ApplyResourceReference(IFundamentalUIComponent component, Style style, ITheme theme,
        ResourceReference reference)
    {
        // theme can be null here: an inline <X.Styles> is attached during construction (the moment it's added to the
        // Styles collection), before the element is themed. Fall back to the current theme; if it (or the resource) is
        // still unavailable, defer to visual-tree attach - by which point the element is themed AND rooted, so both the
        // theme and the full ancestor chain (a Local resource's owner) are present.
        var activeTheme = theme ?? UIAppContext.Current?.ThemeManager?.CurrentTheme;

        if (activeTheme != null && activeTheme.TryGetResource(component, reference.Name, out var resource))
        {
            component.SetStyleValue(Property, resource, style);
            return;
        }

        if (component is IUIComponent visual)
        {
            void OnAttached(object sender, VisualTreeAttachmentEventArgs e)
            {
                visual.AttachedToVisualTreeEvent -= OnAttached;
                var rootedTheme = UIAppContext.Current?.ThemeManager?.CurrentTheme;
                if (rootedTheme != null && rootedTheme.TryGetResource(component, reference.Name, out var deferred))
                    component.SetStyleValue(Property, deferred, style);
                // Still missing once rooted+themed = a genuinely undefined resource; leave the property unset (visibly
                // empty) rather than throw from inside the attach cascade and take down the whole subtree.
            }

            visual.AttachedToVisualTreeEvent += OnAttached;
        }
    }

    /// <summary>Takes back what <see cref="Apply"/> gave. A component without the property was given nothing.</summary>
    public void Remove(IFundamentalUIComponent component, Style style, ITheme theme)
    {
        var property = AdamantiumPropertyMap.ResolveProperty(component.GetType(), Property);
        if (property == null)
        {
            return;
        }

        switch (Value)
        {
            case BindingBase:
            case Ancestor:
            case Self:
                BindingEngine.ClearOwned(component, property, style);
                component.RemoveStyleValue(Property, style);
                break;
            case ThemeResource:
                ThemeResource.Remove(component, Property, SlotFor(property, style), style);
                break;
            case ObservableResource:
                ObservableResource.Remove(component, Property, SlotFor(property, style), style);
                break;
            default:
                component.RemoveStyleValue(Property, style);
                break;
        }
    }

    public bool Equals(Setter other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        // TargetName is part of a setter's identity: two setters that write the SAME property/value onto DIFFERENT
        // template parts (e.g. a trigger lighting up both scrollbars' IsHitTestVisible) are distinct. Omitting it
        // collapsed them into one dictionary key in the trigger activator, so applying the second tore down the first.
        return Equals(Property, other.Property) && Equals(Value, other.Value) && Equals(TargetName, other.TargetName);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((Setter)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Property, TargetName);
    }
}