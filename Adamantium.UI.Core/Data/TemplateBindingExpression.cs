using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Data;

public class TemplateBindingExpression : BindingExpressionBase
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAdamantiumComponent, List<AdamantiumProperty>> Fed = new();

    public IFundamentalUIComponent Source { get; set; }
   
    public AdamantiumProperty SourceProperty { get; set; }

    public string SourcePropertyName { get; set; }
   
    public TemplateBinding TemplateBinding { get; }
    
    public BindingMode Mode { get; set; }

    public TemplateBindingExpression(TemplateBinding templateBinding)
    {
        TemplateBinding = templateBinding;
        Mode = templateBinding.Mode;
    }

    public TemplateBindingExpression(IFundamentalUIComponent source, IAdamantiumComponent target, string targetProperty, TemplateBinding templateBinding) : this(templateBinding)
    {
        // {TemplateBinding Path} on a part's property: read Path from the templated parent (source) and write the part's
        // declared property (targetProperty). The generator passes propRef.Name (the part attribute) as targetProperty
        // and the markup arg as Path - so source resolves from Path, target from targetProperty. These differ for a
        // cross-name binding (e.g. ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}").
        SourceProperty = source == null ? null : AdamantiumPropertyMap.ResolveProperty(source.GetType(), TemplateBinding.Path);
        SourcePropertyName = TemplateBinding.Path;
        Target = target;
        TargetProperty = target.GetProperty(targetProperty);
    }

    private void OnSourcePropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property != SourceProperty) return;   // this source instance raises PropertyChanged for ALL its properties
        UpdateTarget();
    }

    private void OnTargetPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property != TargetProperty) return;
        UpdateSource();
    }

    public override void EstablishConnection()
    {
        Init();
    }

    // Source and target types need not match ({TemplateBinding OverflowButtonWidth} on a GridLength) - convert exactly
    // as {Binding} does, and skip the write when the value cannot be made to fit rather than poison the slot.
    public override void UpdateSource()
    {
        if (TryCoerce(Target.GetValue(TargetProperty), SourceProperty.PropertyType, out var value))
        {
            Source.SetCurrentValue(SourceProperty, value);
        }
    }

    public override void UpdateTarget()
    {
        if (TryCoerce(Source.GetValue(SourceProperty), TargetProperty.PropertyType, out var value))
        {
            Target.SetValue(TargetProperty, value, ValuePriority.Template);
        }
    }

    private void Init()
    {
        Destroy();
        if (SourceProperty == null && Source != null)
        {
            SourceProperty = AdamantiumPropertyMap.ResolveProperty(Source.GetType(), SourcePropertyName);
        }
        UpdateTarget();
        // Subscribe to the SOURCE INSTANCE's change event, not the property's global AdamantiumProperty.Changed. The
        // latter fires for EVERY control that shares this AdamantiumProperty, so one templated control's Content change
        // would run UpdateTarget() on every {TemplateBinding Content} in the whole app (O(all such bindings) per set - the
        // scroll rebind hot path did this ~200x/frame). The instance event scopes the notification to this binding's own source.
        Source.PropertyChanged += OnSourcePropertyChanged;
        if (Mode == BindingMode.TwoWay)
        {
            Target.PropertyChanged += OnTargetPropertyChanged;
        }

        var fed = Fed.GetValue(Target, static _ => []);
        lock (fed)
        {
            fed.Add(TargetProperty);
        }
    }

    private void Destroy()
    {
        if (Source != null)
        {
            Source.PropertyChanged -= OnSourcePropertyChanged;
        }

        if (Mode == BindingMode.TwoWay && Target != null)
        {
            Target.PropertyChanged -= OnTargetPropertyChanged;
        }

        if (Target != null && Fed.TryGetValue(Target, out var fed))
        {
            lock (fed)
            {
                fed.Remove(TargetProperty);
            }
        }
    }

    /// <summary>Whether a live <c>{TemplateBinding}</c> feeds <paramref name="property"/> of <paramref name="target"/> - the
    /// template slot it writes is then where the value comes from.</summary>
    public static bool Feeds(IAdamantiumComponent target, AdamantiumProperty property)
    {
        if (!Fed.TryGetValue(target, out var fed))
        {
            return false;
        }

        lock (fed)
        {
            return fed.Contains(property);
        }
    }
   
    public override void CloseConnection()
    {
        Destroy();
    }
}