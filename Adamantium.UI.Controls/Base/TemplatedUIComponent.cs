using System;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Controls;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Base;

public class TemplatedUIComponent : InputUIComponent, ITemplatedUIComponent, ITemplateHost
{
    private TemplateResult templateResult;
    private ControlTemplate appliedTemplate;

    public static readonly AdamantiumProperty TemplateProperty =
        AdamantiumProperty.Register(nameof(Template), typeof(ControlTemplate), typeof(MeasurableUIComponent),
            // AffectsMeasure too: a NEW template can change the desired size (as in WPF). Without it a template that
            // arrives AFTER the first measure - e.g. a part's Template set via {TemplateBinding} once the owner supplies
            // it - re-renders but never re-measures, so the part stays 0x0 (the tab close button was invisible).
            new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender, TemplateChangedCallback));

    private static void TemplateChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is TemplatedUIComponent component)
        {
            component.OnTemplateChanged();
        }
    }

    // The callback fires on every write, so rebuild only when the effective template differs. While a theme is applied,
    // Template may be written several times, so the build waits until the theme settles.
    private bool _suspendTemplateBuild;
    private bool _pendingTemplateBuild;

    protected override void ApplyCurrentThemeCore()
    {
        var wasSuspended = _suspendTemplateBuild;   // save/restore so a nested re-theme doesn't build early - only the outermost does
        _suspendTemplateBuild = true;
        base.ApplyCurrentThemeCore();
        _suspendTemplateBuild = wasSuspended;
        if (!wasSuspended && _pendingTemplateBuild)
        {
            _pendingTemplateBuild = false;
            OnTemplateChanged();
        }
    }

    private void OnTemplateChanged()
    {
        if (_suspendTemplateBuild) { _pendingTemplateBuild = true; return; }
        var effective = Template;
        if (ReferenceEquals(effective, appliedTemplate)) return;

        if (appliedTemplate != null) RemoveTemplate();
        appliedTemplate = effective;
        if (effective != null && !DeferTemplate)
        {
            ApplyTemplate();
        }

        // Template parts just changed: re-point any style/element triggers that target named parts at the new tree
        // (and tear down what they held on the old, now-discarded parts) so a runtime template swap stays leak-free.
        ReevaluateTriggersForTemplateChange();
    }
    
    public ControlTemplate Template
    {
        get => GetValue<ControlTemplate>(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }
    
    public IAdamantiumComponent GetTemplateChild(string name)
    {
        if (Template == null || templateResult?.RootComponent == null) return null;

        return templateResult.GetComponentByName(name);
    }

    /// <summary>Hold the template back instead of building it as soon as it is themed. A control that is shown nowhere
    /// inline - a <c>ContextMenu</c>, whose rows live in the popup overlay and which exists only to be right-clicked open -
    /// answers true until it is asked for, and calls <see cref="EnsureTemplate"/> then.</summary>
    protected virtual bool DeferTemplate => false;

    /// <summary>Builds the template that <see cref="DeferTemplate"/> held back. Does nothing if it is already built.</summary>
    protected void EnsureTemplate()
    {
        if (templateResult == null && appliedTemplate != null)
        {
            ApplyTemplate();
        }
    }

    /// <summary>TEMP (leak hunt): templates built, and templated controls ever constructed, since start.</summary>
    public static long TemplatesBuilt, TemplatedControlsMade, TemplatesRemoved;

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        System.Runtime.CompilerServices.StrongBox<int>> RemovesByType = new();

    // ...and WHICH types, counted here rather than through LayoutTrace: that one is behind a gate of its own and came
    // back with 14 events for a window in which 723 templates were built.
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        System.Runtime.CompilerServices.StrongBox<int>> BuildsByType = new();

    public static string DumpBuilds() => Dump(BuildsByType);

    public static string DumpRemoves() => Dump(RemovesByType);

    private static string Dump(System.Collections.Concurrent.ConcurrentDictionary<string,
        System.Runtime.CompilerServices.StrongBox<int>> counts)
    {
        var rows = new List<string>();
        foreach (var pair in counts) rows.Add($"{pair.Value.Value,6}  {pair.Key}");
        rows.Sort((a, b) => int.Parse(b.Trim().Split(' ')[0]).CompareTo(int.Parse(a.Trim().Split(' ')[0])));
        return string.Join("\n", rows.GetRange(0, Math.Min(20, rows.Count)));
    }

    public TemplatedUIComponent() => System.Threading.Interlocked.Increment(ref TemplatedControlsMade);

    private void ApplyTemplate()
    {
        if (Template == null) return;

        Core.Diagnostics.LayoutTrace.Count(GetType(), "*template-build*");
        // TEMP (leak hunt): how many templates a swap BUILDS, against how many templated controls there are. The hunt
        // so far asked who holds what is left over; the better question is why a swap makes several times a whole
        // application's worth of elements in the first place.
        System.Threading.Interlocked.Increment(ref TemplatesBuilt);
        System.Threading.Interlocked.Increment(ref BuildsByType.GetOrAdd(GetType().Name,
            static _ => new System.Runtime.CompilerServices.StrongBox<int>()).Value);
        templateResult = Template.Build(this);
        if (templateResult != null)
        {
            // var overrides = ControlTemplateOverride.GetOverrides(this);
            // if (overrides != null)
            // {
            //     foreach (var @override in overrides)
            //     {
            //         // TODO: add here logic for applying overrides
            //     }
            // }
            
            AddTemplateChild(templateResult.RootComponent);
            OnApplyTemplate();
        }
    }

    private void RemoveTemplate()
    {
        // A prior ApplyTemplate whose Build returned null leaves templateResult null while appliedTemplate is set;
        // nothing to tear down then.
        if (templateResult == null) return;

        // TEMP (leak hunt): a REBUILD, as against a first build. A swap builds 2.4x as many templates as the tree
        // holds; this separates "the control was re-templated" from "a new part was made and templated once".
        System.Threading.Interlocked.Increment(ref TemplatesRemoved);
        System.Threading.Interlocked.Increment(ref RemovesByType.GetOrAdd(GetType().Name,
            static _ => new System.Runtime.CompilerServices.StrongBox<int>()).Value);

        TraverseVisualTreeAndUnload(templateResult.RootComponent);
        // Undo the inheritance link set in AddTemplateChild so the detached old template root stops tracking this
        // control's inherited values (a stale parent would keep pushing DataContext/FontFamily changes into orphaned UI).
        if (templateResult.RootComponent is AdamantiumComponent oldRoot)
            oldRoot.InheritanceParent = null;
        RemoveVisualChildren();
        templateResult.Destroy();
        templateResult = null;
        OnRemoveTemplate();
    }


    // The template boundary is the crux of the two-tree model: parts are attached VISUAL-only,
    // so the logical tree stays shallow and dead-ends here. Inheritance is bridged by InheritanceParent (below); an UP
    // logical walk is bridged by TemplatedParent (UIExtensions.GetLogicalParentOrBridge), set on every part in ControlTemplate.Build.
    protected void AddTemplateChild(IUIComponent child)
    {
        // A visual-only template root gets no inheritance parent otherwise, so template bindings would see no DataContext. An
        // explicit local value still wins.
        if (child is AdamantiumComponent component)
            component.InheritanceParent = this;
        AddVisualChild(child);

        // A visual-only template root is not themed by SetParent, so a templated root (e.g. a ScrollViewer) is themed here.
        if (child is FundamentalUIComponent { IsStyleApplied: false } themedRoot)
            themedRoot.ApplyCurrentTheme();
    }

    public virtual void OnRemoveTemplate()
    {
        
    }
    
    private void TraverseVisualTreeAndUnload(IUIComponent component)
    {
        foreach (var child in component.VisualChildren)
        {
            TraverseVisualTreeAndUnload(child);
        }

        if (component is ObservableUIComponent observableUiComponent)
        {
            observableUiComponent.RaiseEvent(new RoutedEventArgs(UnloadedEvent, component));
        }

        // Mark my own parts discarded, by the id of the template result that built them: the visual walk also reaches moved
        // content, and TemplatedParent is shared with a live items panel.
        if (component is FundamentalUIComponent part && templateResult != null &&
            part.OwningTemplateId == templateResult.Id)
        {
            part.MarkDiscarded();

            // A templated part tears down its own template too, so its inner tree is marked and its bindings close.
            if (part is TemplatedUIComponent nested) nested.RemoveTemplate();
        }
    }

    public virtual void OnApplyTemplate()
    {
    }
}