using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Data;

/// <summary>
/// Live connection for <c>{Ancestor ...}</c>: resolves the nearest matching ancestor of the target (visual tree by
/// default, logical when <see cref="Ancestor.Logical"/>), reads its source property and pushes it to the target. It
/// RE-RESOLVES whenever the target (re)attaches to the tree, so a template rebuild / re-parent / virtualization recycle
/// refreshes the binding instead of leaving it pointed at a stale (or never-found) ancestor - the WPF failure mode.
/// </summary>
public class AncestorBindingExpression : BindingExpressionBase
{
    private readonly Ancestor _def;

    /// <summary>Priority the resolved value is written at. Binding for a normal property; a Setter passes Style/Trigger so
    /// the ancestor value slots into the right band of the value stack.</summary>
    internal ValuePriority Priority { get; set; } = ValuePriority.Binding;

    private IFundamentalUIComponent _source;
    private BindingExpression _path;
    private bool _hooked;

    public AncestorBindingExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, Ancestor def)
    {
        Target = target;
        TargetProperty = targetProperty;
        _def = def;
    }

    public override void EstablishConnection()
    {
        CloseConnection();
        HookTree();
        ResolveAndReport();   // resolves now if the target is already rooted; otherwise an attach event re-runs this
    }

    public override void CloseConnection()
    {
        DetachSource();
        UnhookTree();
    }

    // The element the subscriptions are ON. The target itself may be a Transform or another non-tree component, which
    // has no attach/detach of its own and rides its owner's - and the SAME instance must be used to unsubscribe, so it
    // is kept rather than re-walked (the owner can change between hook and unhook).
    private IFundamentalUIComponent _anchor;


    // Subscribe to the target's own attach/detach so the ancestor is (re)resolved every time the target enters the tree.
    private void HookTree()
    {
        if (_hooked) return;

        _anchor = DataContextSource;
        if (_anchor == null) return;

        if (_def.Logical)
        {
            _anchor.AttachedToLogicalTree += OnLogicalAttached;
            _anchor.DetachedFromLogicalTree += OnLogicalDetached;
        }
        else if (_anchor is IUIComponent visual)
        {
            visual.AttachedToVisualTreeEvent += OnVisualAttached;
            visual.DetachedFromVisualTreeEvent += OnVisualDetached;
        }
        else
        {
            // Non-visual target (a Behavior): it has no visual node, so it re-resolves against its HOST's visual tree.
            // AddLogicalChild (attach to the host) raises the logical-tree event - use that to (re)resolve.
            _anchor.AttachedToLogicalTree += OnLogicalAttached;
            _anchor.DetachedFromLogicalTree += OnLogicalDetached;
        }
        _hooked = true;
    }

    private void UnhookTree()
    {
        if (!_hooked || _anchor == null) return;
        if (_def.Logical)
        {
            _anchor.AttachedToLogicalTree -= OnLogicalAttached;
            _anchor.DetachedFromLogicalTree -= OnLogicalDetached;
        }
        else if (_anchor is IUIComponent visual)
        {
            visual.AttachedToVisualTreeEvent -= OnVisualAttached;
            visual.DetachedFromVisualTreeEvent -= OnVisualDetached;
        }
        else
        {
            _anchor.AttachedToLogicalTree -= OnLogicalAttached;
            _anchor.DetachedFromLogicalTree -= OnLogicalDetached;
        }
        _hooked = false;
        _anchor = null;
    }

    private void OnLogicalAttached(object sender, LogicalTreeAttachmentEventArgs e) => ResolveAndReport();
    private void OnVisualAttached(object sender, VisualTreeAttachmentEventArgs e) => ResolveAndReport();
    private void OnLogicalDetached(object sender, LogicalTreeAttachmentEventArgs e) => OnDetached();
    private void OnVisualDetached(object sender, VisualTreeAttachmentEventArgs e) => OnDetached();

    // Resolve, then classify the outcome. A found source is already Active (or PathError for a missing property - set in
    // Resolve). A MISS is a real "no such ancestor" (PathError - the failure WPF swallowed silently) when the target IS
    // in the tree, or merely NotAttached when it isn't rooted yet (the transient case; a later attach re-runs this).
    private void ResolveAndReport()
    {
        Resolve();
        if (_source != null) return;
        ApplyFallback();   // no source resolved -> push FallbackValue if one was set (else leave the target at its default)
        if (IsTargetAttached())
        {
            Fail($"{{Ancestor}} on {Target?.GetType().Name}.{TargetProperty?.Name}: no {_def.AncestorType?.Name} ancestor found in the {(_def.Logical ? "logical" : "visual")} tree.");
        }
        else
        {
            Status = BindingStatus.NotAttached;
        }
    }

    // No source: run the pipeline with an Unset raw value so a FallbackValue / TargetNullValue (if any) still reaches the
    // target. With neither set this produces Unset and leaves the target at its default.
    private void ApplyFallback()
    {
        if (TargetProperty == null) return;
        var value = RelativeBindingPipeline.Produce(RelativeBindingPipeline.Unset, _def.Converter, _def.ConverterParameter,
            TargetProperty.PropertyType, _def.FallbackValue, _def.TargetNullValue);
        if (!ReferenceEquals(value, RelativeBindingPipeline.Unset)) Target.SetValue(TargetProperty, value, Priority);
    }

    // Asked of the nearest ELEMENT: a Transform (or any other non-tree target) has no place in the tree of its own and
    // is attached exactly when its owner is.
    private bool IsTargetAttached()
    {
        var element = DataContextSource;
        return _def.Logical ? element?.GetLogicalParentOrBridge() != null
             : element is IUIComponent visual ? visual.IsAttachedToVisualTree
             : element != null && NearestElement(element) is { IsAttachedToVisualTree: true };   // non-visual: its host is
    }

    private void OnDetached()
    {
        DetachSource();
        Status = BindingStatus.Detached;
    }

    // Re-walk for the ancestor and rebind. Detaching the old source first means a re-parent (detach then attach) lands
    // cleanly on the NEW ancestor rather than stacking a second subscription on the old one.
    private void Resolve()
    {
        DetachSource();
        _source = FindAncestor();
        if (_source == null) return;   // not rooted yet, or no match - a later attach re-resolves (and warns then)

        if (!HasFirstLink(_source, _def.Path))
        {
            Fail($"{{Ancestor}} on {Target?.GetType().Name}.{TargetProperty?.Name}: {_def.AncestorType?.Name} has no '{_def.Path}' property.");
            return;
        }

        _path ??= new BindingExpression(Target, TargetProperty, new Binding(_def.Path)
        {
            Mode = _def.Mode,
            Converter = _def.Converter,
            ConverterParameter = _def.ConverterParameter,
            FallbackValue = _def.FallbackValue,
            TargetNullValue = _def.TargetNullValue
        }) { Priority = Priority };
        _path.Binding.Source = _source;
        _path.EstablishConnection();
        Status = _path.Status;
    }

    internal static bool HasFirstLink(IAdamantiumComponent root, string path)
    {
        var first = path?.Split('.')[0];
        if (string.IsNullOrEmpty(first))
        {
            return false;
        }

        return root.GetProperty(first) != null || BindingExpression.FindProperty(root.GetType(), first) != null;
    }

    // The first thing UP THE LOGICAL CHAIN that is actually in the visual tree - where a non-visual target's visual
    // walk can start. Several steps, because non-visual nodes nest: a row inside a composite inside a section.
    private static IUIComponent NearestElement(IFundamentalUIComponent from)
    {
        for (var cur = from.LogicalParent; cur != null; cur = cur.LogicalParent)
        {
            if (cur is IUIComponent element) return element;
        }

        return null;
    }

    private IFundamentalUIComponent FindAncestor()
    {
        // The walk starts from the nearest ELEMENT: the target may be a Transform, which is not in the tree at all and
        // whose ancestors are its owner's.
        var anchor = DataContextSource;
        if (_def.AncestorType == null || anchor == null) return null;
        var skip = _def.Skip;

        if (_def.Logical)
        {
            // Logical walk BRIDGES template boundaries via TemplatedParent (GetLogicalParentOrBridge) - otherwise it
            // dead-ends at each container's template part and never reaches the ItemsControl.
            for (var cur = anchor.GetLogicalParentOrBridge(); cur != null; cur = cur.GetLogicalParentOrBridge())
            {
                if (_def.Stop != null && _def.Stop.IsInstanceOfType(cur)) return null;
                if (Matches(cur) && skip-- <= 0) return cur;
            }
        }
        else
        {
            // Visual walk from the VisualParent, or for a non-visual target from its nearest logical element host, which
            // may be several steps up.
            var start = anchor is IUIComponent visual ? visual.VisualParent : NearestElement(anchor);
            for (var cur = start; cur != null; cur = cur.VisualParent)
            {
                if (_def.Stop != null && _def.Stop.IsInstanceOfType(cur)) return null;
                if (Matches(cur) && skip-- <= 0) return cur;
            }
        }
        return null;
    }

    // is-a match (a base type / interface matches subtypes, like the style selectors), plus an optional Name filter.
    private bool Matches(IFundamentalUIComponent candidate)
    {
        if (!_def.AncestorType.IsInstanceOfType(candidate)) return false;
        return string.IsNullOrEmpty(_def.Name) || candidate.Name == _def.Name;
    }

    public override void UpdateTarget()
    {
        if (_source != null)
        {
            _path?.UpdateTarget();
        }
    }

    public override void UpdateSource()
    {
        if (_source != null)
        {
            _path?.UpdateSource();
        }
    }

    private void DetachSource()
    {
        BindingUpdateQueue.Remove(this);   // a re-resolve must not be applied to the old source by a later flush
        if (_path != null)
        {
            _path.Forget();
            _path.Binding.Source = null;
        }

        _source = null;
    }
}
